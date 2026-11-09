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
public sealed class SettingsStore(IOptionsMonitor<DicomOptions> dicom, IOptionsMonitor<AgentOptions> agent)
{
    public static string FilePath => Path.Combine(AgentPaths.DataDirectory, "settings.json");
    public AgentSettings Current => new(dicom.CurrentValue, agent.CurrentValue);
    public bool RestartRequired { get; private set; }
    public async Task SaveAsync(AgentSettings settings, CancellationToken ct)
    {
        var errors = ConfigurationRules.Validate(settings.Dicom);
        if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
        var path = string.IsNullOrEmpty(settings.Dicom.StoragePath) ? AgentPaths.DefaultStorage : settings.Dicom.StoragePath;
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, ".probe-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(probe, "", ct); File.Delete(probe);
        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), ct);
        File.Move(temp, FilePath, true);
        RestartRequired = true;
    }
}
