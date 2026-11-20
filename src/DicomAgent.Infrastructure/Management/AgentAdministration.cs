using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Infrastructure.Configuration;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Storage;
namespace DicomAgent.Infrastructure.Management;

public sealed class AgentAdministration(SettingsStore settings, IInstanceRepository repository, DicomOptions active,
    ReceiverState state, InstanceStore storage) : IAgentAdministration
{
    public AgentSettings Settings => settings.Current;
    public long MaxImportBytes => active.MaxInstanceBytes;
    public Task SaveAsync(AgentSettings value, CancellationToken ct) => settings.SaveAsync(value, ct);
    public async Task<HistoryItem[]> HistoryAsync(CancellationToken ct)
    {
        try { return await repository.RecentAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return []; }
    }
    public async Task<AgentStatus> StatusAsync(CancellationToken ct)
    {
        (long Count, DateTime? Last) summary;
        try { summary = await repository.SummaryAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var files = Directory.Exists(storage.Root) ? Directory.GetFiles(storage.Root, "*.dcm", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }) : [];
            summary = (files.LongLength, files.Length == 0 ? null : files.Max(File.GetCreationTimeUtc));
        }
        return new(state.State, active, summary.Count, summary.Last, state.Errors, settings.RestartRequired);
    }
}
