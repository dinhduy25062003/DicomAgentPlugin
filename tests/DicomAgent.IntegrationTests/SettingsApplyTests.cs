using System.Text.Json;
using System.Diagnostics;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure.Configuration;
using FellowOakDicom.Network;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class SettingsApplyTests
{
    [Fact]
    public async Task ApplyPreparesFolderStopsServiceWritesSettingsAndStartsWithoutManualCommands()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = new TestFixture();
        var settings = new AgentSettings(fixture.Options, new()) { LocalApi = new() { Port = TestFixture.FreePort() } };
        settings.Dicom.StoragePath = Path.Combine(AgentPaths.DefaultStorage, "new images");
        var commands = new List<string>();
        Task<string> Run(string name, string[] args)
        {
            commands.Add(name + " " + string.Join(" ", args));
            if (name == "systemctl" && args[0] == "stop") Assert.False(File.Exists(SettingsStore.FilePath));
            if (name == "systemctl" && args[0] == "start")
                Assert.Equal(settings.Dicom.StoragePath, JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(SettingsStore.FilePath))!.Dicom.StoragePath);
            return Task.FromResult("# ACL backup\n");
        }
        var dropIn = Path.Combine(fixture.Data, "override.conf");
        await new InstalledSettingsApplier(dropIn, Run, "1000").ApplyAsync(settings);
        Assert.Contains(commands, line => line.Contains("u:dicom-agent:rwx,d:u:dicom-agent:rwx"));
        Assert.Contains(commands, line => line.Contains("u:1000:rx,d:u:1000:rx"));
        Assert.Contains("ReadWritePaths=\"" + settings.Dicom.StoragePath + "\"", await File.ReadAllTextAsync(dropIn));
        Assert.True(commands.IndexOf("systemctl stop dicom-agent.service") < commands.IndexOf("systemctl start dicom-agent.service"));
        Assert.DoesNotContain(Directory.GetFiles(fixture.Data), path => Path.GetFileName(path).StartsWith(".settings-acl-"));
    }

    [Fact]
    public async Task FailedStartRestoresPreviousSettingsAndSandboxThenStartsOldService()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = new TestFixture();
        var original = new AgentSettings(fixture.Options, new());
        await SettingsStore.WriteAsync(original, default);
        var before = await File.ReadAllBytesAsync(SettingsStore.FilePath);
        var dropIn = Path.Combine(fixture.Data, "override.conf");
        const string oldOverride = "[Service]\n# previous setting\n";
        await File.WriteAllTextAsync(dropIn, oldOverride);
        var changed = JsonSerializer.Deserialize<AgentSettings>(JsonSerializer.Serialize(original))!;
        changed.Dicom.AeTitle = "CHANGED";
        var starts = 0;
        var restoredAcl = false;
        Task<string> Run(string name, string[] args)
        {
            if (name == "setfacl" && args[0].StartsWith("--restore=")) restoredAcl = true;
            if (name == "systemctl" && args[0] == "start" && ++starts == 1) throw new IOException("Simulated service start failure");
            return Task.FromResult("# previous ACL\n");
        }
        var error = await Assert.ThrowsAsync<IOException>(() => new InstalledSettingsApplier(dropIn, Run).ApplyAsync(changed));
        Assert.Contains("Đã khôi phục", error.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(SettingsStore.FilePath));
        Assert.Equal(oldOverride, await File.ReadAllTextAsync(dropIn));
        Assert.Equal(2, starts);
        Assert.True(restoredAcl);
    }

    [Fact]
    public async Task InvalidStorageDoesNotStopServiceOrChangeConfiguration()
    {
        await using var fixture = new TestFixture();
        var settings = new AgentSettings(fixture.Options, new());
        settings.Dicom.StoragePath = fixture.Data;
        var called = false;
        Task<string> Run(string name, string[] args)
        {
            called = true;
            return Task.FromResult("");
        }
        await Assert.ThrowsAsync<ArgumentException>(() => new InstalledSettingsApplier(Path.Combine(fixture.Data, "override.conf"), Run).ApplyAsync(settings));
        Assert.False(called);
        Assert.False(File.Exists(SettingsStore.FilePath));
    }

    [Fact]
    public void HomeStorageOverrideExposesOnlySelectedFolderAndEscapesSystemdSpecifiers()
    {
        var settings = new AgentSettings(new(), new());
        settings.Dicom.StoragePath = "/home/duynguyen/DicomImg/";
        if (!OperatingSystem.IsLinux()) return;
        var path = InstalledSettingsApplier.StorageDirectory(settings);
        var text = InstalledSettingsApplier.LinuxStorageOverride(path);
        Assert.Contains("ProtectHome=tmpfs", text);
        Assert.Contains("BindPaths=\"/home/duynguyen/DicomImg", text);
        Assert.DoesNotContain("ProtectHome=false", text);
        Assert.Contains("100%% images", InstalledSettingsApplier.LinuxStorageOverride("/home/operator/100% images"));
        settings.Dicom.StoragePath = "/home/duynguyen";
        Assert.Throws<ArgumentException>(() => InstalledSettingsApplier.StorageDirectory(settings));
        settings.Dicom.StoragePath = "/etc";
        Assert.Throws<ArgumentException>(() => InstalledSettingsApplier.StorageDirectory(settings));
    }

    [Fact]
    public async Task RunningServiceStorageCheckDoesNotChangeSettingsOrRequireRestart()
    {
        await using var fixture = new TestFixture();
        await fixture.StartAsync();
        using var api = fixture.Api();
        await api.StartAsync(default);
        try
        {
            var client = new ManagementClient();
            Assert.True((await client.SendAsync(new("storage-check"))).Success);
            Assert.False(File.Exists(SettingsStore.FilePath));
            Assert.False((await client.SendAsync(new("status"))).Status!.RestartRequired);
            Assert.Empty(Directory.GetFiles(fixture.Options.StoragePath, ".probe-*"));
        }
        finally { await api.StopAsync(default); }
    }

    [Fact]
    public async Task FailedRunningServiceProbeRollsBackEvenAfterServiceStartSucceeded()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = new TestFixture();
        var settings = new AgentSettings(fixture.Options, new());
        await SettingsStore.WriteAsync(settings, default);
        var before = await File.ReadAllBytesAsync(SettingsStore.FilePath);
        settings.Dicom.AeTitle = "NEW_AE";
        var starts = 0;
        var running = false;
        string? activeAe = null;
        Task<string> Run(string name, string[] args)
        {
            if (name == "systemctl" && args[0] == "stop") running = false;
            if (name == "systemctl" && args[0] == "start")
            {
                starts++;
                if (!running) activeAe = JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(SettingsStore.FilePath))!.Dicom.AeTitle;
                running = true;
            }
            return Task.FromResult("# ACL\n");
        }
        await Assert.ThrowsAsync<IOException>(() => new InstalledSettingsApplier(Path.Combine(fixture.Data, "override.conf"), Run,
            verifyRunning: () => throw new IOException("Service cannot write folder")).ApplyAsync(settings));
        Assert.Equal(before, await File.ReadAllBytesAsync(SettingsStore.FilePath));
        Assert.Equal(2, starts);
        Assert.Equal("DICOM_AGENT", activeAe);
    }

    [Fact]
    public void ElevatedInputIsLimitedToAppTemporaryFilesAndRejectsLinks()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine("/tmp", "dicom-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "settings.json");
            File.WriteAllText(input, "{}");
            DicomAgent.Host.Configuration.InstalledSettingsCommand.ValidateInput(input);
            Assert.Throws<ArgumentException>(() => DicomAgent.Host.Configuration.InstalledSettingsCommand.ValidateInput("/etc/settings.json"));
            File.Delete(input);
            File.CreateSymbolicLink(input, "/etc/passwd");
            Assert.Throws<ArgumentException>(() => DicomAgent.Host.Configuration.InstalledSettingsCommand.ValidateInput(input));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task HostApplyCommandRejectsNonAdministratorWithoutChangingServiceData()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root") return;
        await using var fixture = new TestFixture();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DicomAgent.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";
        var host = Path.Combine(root.FullName, "src", "DicomAgent.Host", "bin", configuration, "net8.0", "DicomAgent.Host.dll");
        var input = Path.Combine(fixture.Data, "input.json");
        await File.WriteAllTextAsync(input, "{}");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(host);
        start.ArgumentList.Add("--apply-settings");
        start.ArgumentList.Add(input);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(true); }
        Assert.Equal(1, process.ExitCode);
        var result = JsonSerializer.Deserialize<ManagementResponse>(await File.ReadAllTextAsync(input + ".result"))!;
        Assert.False(result.Success);
        Assert.Contains("xác thực quản trị", result.Error);
        Assert.False(File.Exists(SettingsStore.FilePath));
        Assert.False(File.Exists(Path.Combine(fixture.Data, "settings-apply.lock")));
    }

    [Fact]
    public async Task FailedStorageProbeDoesNotReplaceSettingsAndReceiverStillWorks()
    {
        await using var fixture = new TestFixture();
        await fixture.StartAsync();
        using var api = fixture.Api();
        await api.StartAsync(default);
        try
        {
            var client = new ManagementClient();
            var settings = (await client.SendAsync(new("settings"))).Settings!;
            settings.Dicom.StoragePath = "";
            Assert.True((await client.SendAsync(new("save", settings))).Success);
            var before = await File.ReadAllBytesAsync(SettingsStore.FilePath);
            var file = Path.Combine(fixture.Data, "not-a-directory");
            await File.WriteAllTextAsync(file, "existing file");
            settings.Dicom.StoragePath = file;
            Assert.False((await client.SendAsync(new("save", settings))).Success);
            Assert.Equal(before, await File.ReadAllBytesAsync(SettingsStore.FilePath));
            Assert.Equal(DicomStatus.Success, await fixture.SendAsync());
        }
        finally { await api.StopAsync(default); }
    }
}
