using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
namespace DicomAgent.Infrastructure.Management;

public sealed class ManagementAccess
{
    public const string Header = "X-DicomAgent-Key";
    private string key = "";
    public async Task InitializeAsync(int port, CancellationToken ct)
    {
        key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var path = AgentPaths.DesktopConnectionFile; var temporary = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        try
        {
            await using (var stream = new FileStream(temporary, options))
            {
                await JsonSerializer.SerializeAsync(stream, new DesktopConnection($"http://127.0.0.1:{port}", key), cancellationToken: ct);
                await stream.FlushAsync(ct); stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public bool Authorize(string candidate) => key.Length > 0 && candidate.Length == key.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(candidate));
}
