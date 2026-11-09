using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class HostProcessTests
{
    [Fact]
    public async Task HostRestartsWithPersistentSettingsAndSurvivesManagementClientClosure()
    {
        if (OperatingSystem.IsWindows()) return; // Linux signal/process smoke test; Windows service lifecycle needs Windows validation.
        await using var fixture = new TestFixture();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DicomAgent.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";
        var hostPath = Path.Combine(root.FullName, "src", "DicomAgent.Host", "bin", configuration, "net8.0", "DicomAgent.Host.dll");
        var settingsPath = Path.Combine(fixture.Data, "settings.json");
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(new AgentSettings(fixture.Options, new())));
        var client = new ManagementClient();
        var process = Start(hostPath, fixture.Data);
        try
        {
            await WaitForHostAsync(process);
            Assert.Equal("Running", (await client.SendAsync(new("status"))).Status!.State);
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync(TestFixture.File()));
            // Each request opens and closes its own management connection. Reception remains independent.
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync());
            var settings = (await client.SendAsync(new("settings"))).Settings!;
            settings.Dicom.AeTitle = "AFTER_RESTART";
            Assert.True((await client.SendAsync(new("save", settings))).Success);
            Assert.True((await client.SendAsync(new("status"))).Status!.RestartRequired);
            await StopAsync(process);
            Assert.False(File.Exists(AgentPaths.SocketPath));
            process.Dispose(); process = Start(hostPath, fixture.Data);
            await WaitForHostAsync(process);
            var restarted = (await client.SendAsync(new("status"))).Status!;
            Assert.Equal("AFTER_RESTART", restarted.ActiveConfiguration.AeTitle);
            Assert.False(restarted.RestartRequired);
            Assert.Equal(1, restarted.TotalReceivedFiles);
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync(called: "AFTER_RESTART"));
        }
        finally { if (!process.HasExited) await StopAsync(process); process.Dispose(); }
    }
    private static Process Start(string path, string data)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(path); info.Environment["DICOM_AGENT_DATA"] = data;
        var process = Process.Start(info)!;
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }
    private static async Task WaitForHostAsync(Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!File.Exists(AgentPaths.SocketPath))
        {
            Assert.False(process.HasExited, "Host exited before opening its management socket.");
            await Task.Delay(50, timeout.Token);
        }
    }
    private static async Task StopAsync(Process process)
    {
        Assert.Equal(0, kill(process.Id, 15));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
    }
    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
}
