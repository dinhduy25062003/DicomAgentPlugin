using System.Net.Http.Json;
using System.Text.Json;
using DicomAgent.Application.Models;
namespace DicomAgent.Application.Services;

public sealed class ManagementClient
{
    private const string AccessHeader = "X-DicomAgent-Key";
    private static HttpClient CreateClient() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task<DesktopConnection> ConnectionAsync(CancellationToken ct = default)
    {
        var value = JsonSerializer.Deserialize<DesktopConnection>(await File.ReadAllTextAsync(AgentPaths.DesktopConnectionFile, ct))
            ?? throw new InvalidDataException("Local Host connection is unavailable.");
        if (!Uri.TryCreate(value.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrWhiteSpace(value.Key))
            throw new InvalidDataException("Local Host connection must use HTTP on 127.0.0.1.");
        return value;
    }
    public async Task<ManagementResponse> SendAsync(ManagementRequest request, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var connection = await ConnectionAsync(timeout.Token);
        using var client = CreateClient(); using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(connection.BaseUrl), "api/management/commands"));
        message.Headers.Add(AccessHeader, connection.Key); message.Content = JsonContent.Create(request);
        using var response = await client.SendAsync(message, timeout.Token); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ManagementResponse>(cancellationToken: timeout.Token) ?? throw new InvalidDataException("Empty Host response.");
    }
    public async Task<ManagementResponse> ImportAsync(Guid requestId, string path, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var connection = await ConnectionAsync(timeout.Token);
        using var client = CreateClient(); using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(connection.BaseUrl), $"api/management/captures/{requestId}/files"));
        message.Headers.Add(AccessHeader, connection.Key);
        await using var stream = File.OpenRead(path); message.Content = new StreamContent(stream); message.Content.Headers.ContentLength = stream.Length;
        message.Content.Headers.ContentType = new("application/dicom");
        using var response = await client.SendAsync(message, timeout.Token); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ManagementResponse>(cancellationToken: timeout.Token) ?? throw new InvalidDataException("Empty Host response.");
    }
}
