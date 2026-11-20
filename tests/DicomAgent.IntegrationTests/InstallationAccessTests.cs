using System.Diagnostics;
using Avalonia.Styling;
using DicomAgent.Application.Services;
using DicomAgent.Desktop.Services;
using DicomAgent.Infrastructure.Management;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class InstallationAccessTests
{
    [Fact]
    public async Task LinuxInstallerGrantsExistingSessionReadAccessToDesktopHttpConnection()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = new TestFixture();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DicomAgent.sln"))) root = root.Parent;
        Assert.NotNull(root);
        File.SetUnixFileMode(fixture.Data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        var source = Path.Combine(root.FullName, "deploy", "linux", "configure-operator-access.sh");
        // This UID is deliberately absent from the Host process's groups; permission is a named-user ACL.
        const string operatorUid = "65534";
        await RunAsync("sh", source, fixture.Data, operatorUid);
        var directoryAcl = await RunAsync("getfacl", "-cpn", fixture.Data);
        Assert.Contains("user:65534:r-x", directoryAcl);
        Assert.Contains("default:user:65534:r-x", directoryAcl);
        Assert.Contains("other::---", directoryAcl);
        var access = new ManagementAccess();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await access.InitializeAsync(17890, default);
            var acl = await RunAsync("getfacl", "-cpn", AgentPaths.DesktopConnectionFile);
            Assert.Contains("user:65534:r-x", acl); Assert.Contains("other::---", acl);
            Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(AgentPaths.DesktopConnectionFile) & (UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
        }
        // The inherited read grant must not expose the private request/token directory.
        var captures = fixture.NewCaptureService();
        await captures.SnapshotAsync(default);
        var privateMode = File.GetUnixFileMode(Path.Combine(fixture.Data, "requests"));
        Assert.Equal(UnixFileMode.None, privateMode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
        await RunAsync("sh", source, fixture.Data, operatorUid);
        Assert.False(File.Exists(Path.Combine(fixture.Data, "management.sock")));
    }

    [Fact]
    public void LinuxDesktopStartupCanBeEnabledAndDisabledWithoutServiceAccess()
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = Path.Combine(Path.GetTempPath(), "dicom-startup-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var startup = new DesktopStartupRegistration(path, "/opt/dicom agent/desktop/DicomAgent.Desktop");
            Assert.False(startup.IsEnabled()); startup.SetEnabled(true); Assert.True(startup.IsEnabled());
            var entry = File.ReadAllText(Path.Combine(path, "autostart", "dicom-agent.desktop"));
            Assert.Contains("Exec=\"/opt/dicom agent/desktop/DicomAgent.Desktop\"", entry);
            Assert.Contains("Terminal=false", entry);
            startup.SetEnabled(false); Assert.False(startup.IsEnabled());
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact]
    public void DesktopThemePersistsPerUserAndRecoversFromInvalidPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dicom-theme-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new DesktopThemeSettings(directory);
            Assert.Equal(ThemeVariant.Light, settings.Load());
            settings.Save(ThemeVariant.Dark);
            Assert.Equal(ThemeVariant.Dark, new DesktopThemeSettings(directory).Load());
            settings.Save(ThemeVariant.Light);
            Assert.Equal(ThemeVariant.Light, new DesktopThemeSettings(directory).Load());
            Assert.Throws<ArgumentException>(() => settings.Save(ThemeVariant.Default));
            var path = Path.Combine(directory, "DicomAgent", "appearance.json");
            File.WriteAllText(path, "invalid JSON");
            Assert.Equal(ThemeVariant.Light, settings.Load());
            settings.Save(ThemeVariant.Dark);
            Assert.Equal(ThemeVariant.Dark, settings.Load());
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task<string> RunAsync(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
