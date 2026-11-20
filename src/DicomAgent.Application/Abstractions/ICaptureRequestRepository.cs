using DicomAgent.Application.Models;

namespace DicomAgent.Application.Abstractions;

public interface ICaptureRequestRepository
{
    Task<CaptureRequestStore> LoadAsync(CancellationToken ct);
    Task BindTenantAsync(string tenant, CancellationToken ct);
    Task SaveAsync(StoredCapture value, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
