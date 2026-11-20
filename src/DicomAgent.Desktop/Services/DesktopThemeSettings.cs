using System.Text.Json;
using Avalonia.Styling;

namespace DicomAgent.Desktop.Services;

public sealed class DesktopThemeSettings(string? configDirectory = null)
{
    private sealed record Preferences(string Theme);
    private string FilePath
    {
        get
        {
            var root = configDirectory ?? Environment.GetEnvironmentVariable("DICOM_AGENT_DESKTOP_CONFIG");
            if (string.IsNullOrWhiteSpace(root))
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                root = OperatingSystem.IsWindows()
                    ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    : !string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg)
                        ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            return Path.Combine(root, "DicomAgent", "appearance.json");
        }
    }
    public ThemeVariant Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return ThemeVariant.Light;
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath));
            return preferences?.Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ThemeVariant.Light;
        }
    }
    public void Save(ThemeVariant theme)
    {
        if (theme != ThemeVariant.Light && theme != ThemeVariant.Dark) throw new ArgumentException("Choose Light or Dark.", nameof(theme));
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(theme == ThemeVariant.Dark ? "Dark" : "Light")));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
