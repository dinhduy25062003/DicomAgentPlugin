using DicomAgent.Application.Models;

namespace DicomAgent.Application.Abstractions;

public interface ICaptureRequestService
{
    Task<CaptureSnapshot> SnapshotAsync(CancellationToken ct);
    Task<CaptureRequest> ReceiveAsync(Guid id, string tenant, string authorization, CaptureInput input, CancellationToken ct);
    Task<CaptureRequest> ReadForWebAsync(Guid id, string tenant, string authorization, CancellationToken ct);
    Task<CaptureSession?> CurrentSessionAsync(CancellationToken ct);
    Task ActivateAsync(Guid? id, CancellationToken ct);
    Task AttachAsync(Guid id, CapturedDicomFile file, bool requireActive, CancellationToken ct, Guid? sessionGeneration = null);
    Task<ImagePreview> PreviewAsync(Guid id, Guid fileId, CancellationToken ct);
    Task RemoveAsync(Guid id, Guid? fileId, CancellationToken ct);
    Task QueueUploadAsync(Guid id, CancellationToken ct, Guid[]? expectedFileIds = null);
    Task ResolveAsync(Guid id, bool alreadyUploaded, CancellationToken ct);
    Task ProcessUploadAsync(Guid id, CancellationToken ct);
    Task RunUploadsAsync(CancellationToken ct);
}
