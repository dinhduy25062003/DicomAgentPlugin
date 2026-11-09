using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure.Configuration;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace DicomAgent.Infrastructure.Management;

// IPC access is authorization: operator group membership grants both history access and configuration rights.
public sealed class ManagementServer(SettingsStore settings, IInstanceRepository repository, DicomOptions active,
    ReceiverState state, InstanceStore storage, ILogger<ManagementServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (OperatingSystem.IsWindows())
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await using var pipe = CreateWindowsPipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleAsync(pipe, stoppingToken);
            }
        }
        else
        {
            // The installer owns the parent directory; never delete another active Host's endpoint.
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            if (File.Exists(AgentPaths.SocketPath)) File.Delete(AgentPaths.SocketPath);
            socket.Bind(new UnixDomainSocketEndPoint(AgentPaths.SocketPath));
            File.SetUnixFileMode(AgentPaths.SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
            socket.Listen(8);
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var client = await socket.AcceptAsync(stoppingToken);
                    await using var stream = new NetworkStream(client, false);
                    await HandleAsync(stream, stoppingToken);
                }
            }
            finally { File.Delete(AgentPaths.SocketPath); }
        }
    }
    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsPipe()
    {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        var sids = new List<SecurityIdentifier> { new(WellKnownSidType.LocalSystemSid, null), new(WellKnownSidType.BuiltinAdministratorsSid, null) };
        try { sids.Add((SecurityIdentifier)new NTAccount("DicomAgentOperators").Translate(typeof(SecurityIdentifier))); }
        catch (IdentityNotMappedException) { /* Development without installation permits only the current identity and administrators. */ }
        foreach (var sid in sids)
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        // Deny remote network logons; this endpoint is strictly local.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(AgentPaths.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security);
    }
    private async Task HandleAsync(Stream stream, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var request = await ManagementWire.ReadAsync<ManagementRequest>(stream, timeout.Token);
            ManagementResponse response;
            switch (request.Operation)
            {
                case "settings": response = new(true, Settings: settings.Current); break;
                case "save" when request.Settings is not null:
                    await settings.SaveAsync(request.Settings, timeout.Token); response = new(true); break;
                case "history":
                    try { response = new(true, History: await repository.RecentAsync(timeout.Token)); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { response = new(true, "History is unavailable; files remain in storage.", History: []); }
                    break;
                case "status":
                    (long Count, DateTime? Last) summary;
                    try { summary = await repository.SummaryAsync(timeout.Token); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        var files = Directory.Exists(storage.Root) ? Directory.GetFiles(storage.Root, "*.dcm", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }) : [];
                        summary = (files.LongLength, files.Length == 0 ? null : files.Max(File.GetCreationTimeUtc));
                    }
                    response = new(true, Status: new(state.State, active, summary.Count, summary.Last, state.Errors, settings.RestartRequired)); break;
                default: response = new(false, "Unknown management operation."); break;
            }
            await ManagementWire.WriteAsync(stream, response, timeout.Token);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Management request failed ({ErrorType}).", ex.GetType().Name);
            try { await ManagementWire.WriteAsync(stream, new ManagementResponse(false, "Request failed. Check settings, service permissions and database availability."), timeout.Token); }
            catch (Exception sendError) when (sendError is IOException or OperationCanceledException or ObjectDisposedException) { logger.LogDebug("Management client disconnected."); }
        }
    }
}
