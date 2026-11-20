using Microsoft.Win32;

namespace DicomAgent.Desktop.Services;

public sealed class DesktopStartupRegistration(string? configDirectory = null, string? executable = null)
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "DicomAgent.Desktop";
    private string AutostartPath
    {
        get
        {
            var configured = configDirectory ?? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
                ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "autostart", "dicom-agent.desktop");
        }
    }
    public bool IsEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(EntryName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        return File.Exists(AutostartPath) && !File.ReadLines(AutostartPath).Any(line =>
            line.Equals("Hidden=true", StringComparison.OrdinalIgnoreCase) || line.Equals("X-GNOME-Autostart-enabled=false", StringComparison.OrdinalIgnoreCase));
    }
    public void SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (enabled) key.SetValue(EntryName, QuoteWindows(executable ?? Environment.ProcessPath ?? throw new IOException("Application path is unavailable.")));
            else key.DeleteValue(EntryName, throwOnMissingValue: false);
            return;
        }
        var path = AutostartPath;
        if (!enabled) { File.Delete(path); return; }
        var command = executable ?? (File.Exists("/usr/bin/dicom-agent-desktop") ? "/usr/bin/dicom-agent-desktop" : Environment.ProcessPath)
            ?? throw new IOException("Application path is unavailable.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var escaped = command.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`").Replace("%", "%%");
        var content = $"[Desktop Entry]\nType=Application\nName=DICOM Desktop Agent\nExec=\"{escaped}\"\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        var temporary = path + ".tmp";
        try { File.WriteAllText(temporary, content); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string QuoteWindows(string path) => "\"" + path + "\"";
}
