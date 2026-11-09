using System.Security.Cryptography;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure.Dicom;
using FellowOakDicom;
using Microsoft.Extensions.Logging;

namespace DicomAgent.Infrastructure.Storage;

public sealed record StoreResult(string Path, bool Duplicate, bool HistoryPending);
public sealed class InstanceConflictException() : IOException("The SOP Instance UID already exists with different content.");

public sealed class InstanceStore(IInstanceRepository repository, DicomOptions options, AgentOptions agent,
    ILogger<InstanceStore> logger, ReceiverState state)
{
    // A single Host owns each storage root. Serialize commits and reconciliation, including duplicate checks.
    private readonly SemaphoreSlim gate = new(1, 1);
    public string Root { get; } = Path.GetFullPath(string.IsNullOrEmpty(options.StoragePath) ? AgentPaths.DefaultStorage : options.StoragePath);
    public static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
    public async Task<StoreResult> SaveAsync(DicomFile file, CancellationToken ct)
    {
        var item = MetadataExtractor.Extract(file);
        await gate.WaitAsync(ct);
        string? temp = null;
        try
        {
            RejectLinks(Root);
            CreateStorageDirectory(Root);
            var received = DateTime.UtcNow;
            var target = ConfigurationRules.InstancePath(Root, received, item.StudyInstanceUid, item.SeriesInstanceUid, item.SopInstanceUid);
            var directory = Path.GetDirectoryName(target)!;
            RejectLinks(directory); CreateStorageDirectory(directory); RejectLinks(directory);
            temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".partial");
            await file.SaveAsync(temp);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            ct.ThrowIfCancellationRequested();
            if (new FileInfo(temp).Length > options.MaxInstanceBytes) throw new IOException("Instance exceeds configured size limit.");
            // Force file contents to stable storage before publishing the final filename.
            using (var flush = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) flush.Flush(true);
            var hash = await HashAsync(temp, ct);
            var existing = Directory.EnumerateFiles(Root, item.SopInstanceUid + ".dcm", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }).FirstOrDefault();
            var duplicate = existing is not null;
            if (existing is not null)
            {
                RejectLinks(existing);
                var existingHash = await HashAsync(existing, ct);
                if (existingHash != hash)
                {
                    // Part 10 source-AE/implementation headers may differ across retransmissions.
                    // Compare canonical Part 10 encodings built from the datasets without modifying the retained file.
                    var oldFile = await DicomFile.OpenAsync(existing);
                    if (await DatasetHashAsync(oldFile, directory, ct) != await DatasetHashAsync(file, directory, ct))
                        throw new InstanceConflictException();
                }
                hash = existingHash;
                target = existing;
            }
            else File.Move(temp, target, false);
            item.StorageRoot = Root; item.FilePath = target; item.FileHash = hash;
            item.FileSize = new FileInfo(target).Length;
            item.ReceivedAtUtc = duplicate ? File.GetCreationTimeUtc(target) : received;
            var pending = false;
            if (agent.EnableFileHistory)
            {
                try { await repository.UpsertAsync(item, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    pending = true; state.Error("File saved; history pending reconciliation.");
                    logger.LogError("History persistence failed ({ErrorType}); committed DICOM retained.", ex.GetType().Name);
                }
            }
            return new StoreResult(target, duplicate, pending);
        }
        finally
        {
            try { if (temp is not null && File.Exists(temp)) File.Delete(temp); }
            finally { gate.Release(); }
        }
    }
    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (!agent.EnableFileHistory || !Directory.Exists(Root)) return;
        await gate.WaitAsync(ct);
        try
        {
            RejectLinks(Root);
            var indexed = await repository.IndexedPathsAsync(Root, ct);
            foreach (var path in Directory.EnumerateFiles(Root, "*.dcm", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
            {
                ct.ThrowIfCancellationRequested();
                if (indexed.Contains(path)) continue;
                RejectLinks(path);
                var file = await DicomFile.OpenAsync(path);
                var item = MetadataExtractor.Extract(file);
                item.StorageRoot = Root; item.FilePath = path; item.FileHash = await HashAsync(path, ct);
                item.FileSize = new FileInfo(path).Length; item.ReceivedAtUtc = File.GetCreationTimeUtc(path);
                await repository.UpsertAsync(item, ct);
            }
        }
        finally { gate.Release(); }
    }
    private static void CreateStorageDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
    }
    private static async Task<string> DatasetHashAsync(DicomFile file, string directory, CancellationToken ct)
    {
        var temp = Path.Combine(directory, ".canonical-" + Guid.NewGuid().ToString("N") + ".partial");
        try { await new DicomFile(file.Dataset).SaveAsync(temp); return await HashAsync(temp, ct); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void RejectLinks(string path)
    {
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null; current = Directory.GetParent(current.FullName))
            if (current.LinkTarget is not null) throw new IOException("Symbolic links are not allowed in storage paths.");
    }
}
