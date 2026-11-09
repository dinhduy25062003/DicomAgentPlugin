using DicomAgent.Domain.Entities;
using DicomAgent.Application.Models;
namespace DicomAgent.Application.Abstractions;
public interface IInstanceRepository
{
    Task<HashSet<string>> IndexedPathsAsync(string storageRoot, CancellationToken ct);
    Task InitializeAsync(CancellationToken ct);
    Task UpsertAsync(DicomInstance instance, CancellationToken ct);
    Task<HistoryItem[]> RecentAsync(CancellationToken ct);
    Task<(long Count, DateTime? Last)> SummaryAsync(CancellationToken ct);
}
