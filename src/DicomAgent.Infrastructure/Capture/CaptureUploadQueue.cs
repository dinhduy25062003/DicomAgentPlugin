using System.Threading.Channels;
using DicomAgent.Application.Abstractions;

namespace DicomAgent.Infrastructure.Capture;

public sealed class CaptureUploadQueue : ICaptureUploadQueue
{
    private readonly Channel<Guid> channel = Channel.CreateBounded<Guid>(1);
    public bool TryEnqueue(Guid id) => channel.Writer.TryWrite(id);
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}
