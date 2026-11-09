using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using DicomAgent.Application.Models;

namespace DicomAgent.Application.Services;

public sealed class ManagementClient
{
    public async Task<ManagementResponse> SendAsync(ManagementRequest request, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using Stream stream = await ConnectAsync(timeout.Token);
        await ManagementWire.WriteAsync(stream, request, timeout.Token);
        return await ManagementWire.ReadAsync<ManagementResponse>(stream, timeout.Token);
    }
    private static async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", AgentPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(ct); return pipe; } catch { pipe.Dispose(); throw; }
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(AgentPaths.SocketPath), ct); return new NetworkStream(socket, true); }
        catch { socket.Dispose(); throw; }
    }
}
public static class ManagementWire
{
    private const int MaxLength = 1048576;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaxLength) throw new InvalidDataException("IPC message too large.");
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaxLength) throw new InvalidDataException("Invalid IPC message length.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty IPC message.");
    }
}
