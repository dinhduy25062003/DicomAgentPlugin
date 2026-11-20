using DicomAgent.Application.Models;
namespace DicomAgent.Application.Abstractions;
public interface IAgentAdministration
{
    AgentSettings Settings { get; }
    long MaxImportBytes { get; }
    Task CheckStorageAsync(CancellationToken ct);
    Task SaveAsync(AgentSettings settings, CancellationToken ct);
    Task<HistoryItem[]> HistoryAsync(CancellationToken ct);
    Task<AgentStatus> StatusAsync(CancellationToken ct);
}
