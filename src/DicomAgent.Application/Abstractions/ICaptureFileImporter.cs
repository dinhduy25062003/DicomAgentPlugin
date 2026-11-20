namespace DicomAgent.Application.Abstractions;
public interface ICaptureFileImporter
{
    Task ImportAsync(Guid id, Stream source, long length, CancellationToken ct);
}
