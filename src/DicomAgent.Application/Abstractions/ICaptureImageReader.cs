using DicomAgent.Application.Models;

namespace DicomAgent.Application.Abstractions;

public interface ICaptureImageReader
{
    Task<ImagePreview> PreviewAsync(string path, CancellationToken ct);
}
