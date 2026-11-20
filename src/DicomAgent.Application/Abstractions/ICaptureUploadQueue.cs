namespace DicomAgent.Application.Abstractions;

public interface ICaptureUploadQueue
{
    bool TryEnqueue(Guid id);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}
