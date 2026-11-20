using System.Text.Json;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using Microsoft.Extensions.Options;

namespace DicomAgent.Infrastructure.Configuration;

public sealed class DicomOptionsValidator : IValidateOptions<DicomOptions>
{
    public ValidateOptionsResult Validate(string? name, DicomOptions options)
    {
        var errors = ConfigurationRules.Validate(options);
        return errors.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
public sealed class SettingsStore(IOptionsMonitor<DicomOptions> dicom, IOptionsMonitor<AgentOptions> agent, IOptionsMonitor<LocalApiOptions> localApi)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public static string FilePath => Path.Combine(AgentPaths.DataDirectory, "settings.json");
    public AgentSettings Current => new(dicom.CurrentValue, agent.CurrentValue) { LocalApi = localApi.CurrentValue };
    public bool RestartRequired { get; private set; }
    public async Task SaveAsync(AgentSettings settings, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await WriteAsync(settings, ct);
            RestartRequired = true;
        }
        finally { gate.Release(); }
    }
    public static async Task WriteAsync(AgentSettings settings, CancellationToken ct)
    {
        CaptureRules.Validate(settings.LocalApi);
        var errors = ConfigurationRules.Validate(settings.Dicom);
        if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
        var path = string.IsNullOrEmpty(settings.Dicom.StoragePath) ? AgentPaths.DefaultStorage : settings.Dicom.StoragePath;
        await ProbeStorageAsync(path, ct);
        var temp = FilePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), ct);
            File.Move(temp, FilePath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
    public static async Task ProbeStorageAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, ".probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(probe, "", ct);
            File.Delete(probe);
        }
        finally
        {
            if (File.Exists(probe)) File.Delete(probe);
        }
    }

}
