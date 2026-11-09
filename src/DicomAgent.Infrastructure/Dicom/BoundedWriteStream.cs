namespace DicomAgent.Infrastructure.Dicom;
internal sealed class BoundedWriteStream(Stream inner, long limit) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) { Check(value); inner.SetLength(value); }
    public override void Write(byte[] buffer, int offset, int count) { Check(Position + count); inner.Write(buffer, offset, count); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    { Check(Position + count); return inner.WriteAsync(buffer, offset, count, ct); }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    { Check(Position + buffer.Length); return inner.WriteAsync(buffer, ct); }
    private void Check(long size) { if (size > limit) throw new IOException("DICOM receive size limit exceeded."); }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
