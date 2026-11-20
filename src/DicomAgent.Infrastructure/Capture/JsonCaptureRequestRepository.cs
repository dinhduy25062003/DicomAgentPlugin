using System.Text.Json;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;

namespace DicomAgent.Infrastructure.Capture;

public sealed class JsonCaptureRequestRepository : ICaptureRequestRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string directory = Path.Combine(AgentPaths.DataDirectory, "requests");
    private void EnsureDirectory()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public async Task<CaptureRequestStore> LoadAsync(CancellationToken ct)
    {
        EnsureDirectory();
        var binding = Path.Combine(directory, "tenant-binding");
        var tenant = File.Exists(binding) ? (await File.ReadAllTextAsync(binding, ct)).Trim() : null;
        var records = new List<StoredCapture>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            records.Add(JsonSerializer.Deserialize<StoredCapture>(await File.ReadAllTextAsync(path, ct), Json)
                ?? throw new InvalidDataException("Request storage is invalid."));
        return new(tenant, records.ToArray());
    }
    public async Task BindTenantAsync(string tenant, CancellationToken ct)
    {
        EnsureDirectory(); var path = Path.Combine(directory, "tenant-binding");
        try
        {
            await using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(tenant), ct);
                await stream.FlushAsync(ct); stream.Flush(true);
            }
            File.Move(path + ".tmp", path, true);
        }
        finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
    }
    public async Task SaveAsync(StoredCapture value, CancellationToken ct)
    {
        EnsureDirectory(); var path = Path.Combine(directory, value.Request.Id.ToString("N") + ".json");
        var temp = path + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temp, options))
            {
                await JsonSerializer.SerializeAsync(stream, value, Json, ct);
                await stream.FlushAsync(ct); stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); File.Delete(Path.Combine(directory, id.ToString("N") + ".json"));
        return Task.CompletedTask;
    }
}
