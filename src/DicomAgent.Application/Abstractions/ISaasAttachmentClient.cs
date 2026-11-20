using DicomAgent.Application.Models;

namespace DicomAgent.Application.Abstractions;

public interface ISaasAttachmentClient
{
    Task<AttachmentUploadResult> UploadAsync(CaptureRequest request, string serverUrl, string authorization, CancellationToken ct);
}
