using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure.Configuration;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class ManagementTests
{
    [Fact]
    public async Task LocalIpcSavesSettingsAndClientDisconnectDoesNotStopReceiver()
    {
        await using var fixture = new TestFixture();
        await fixture.StartAsync();
        using var server = new ManagementServer(fixture.Provider.GetRequiredService<SettingsStore>(), fixture.Repository,
            fixture.Options, fixture.Provider.GetRequiredService<ReceiverState>(), fixture.Store, NullLogger<ManagementServer>.Instance);
        await server.StartAsync(default);
        try
        {
            var client = new ManagementClient();
            var status = await client.SendAsync(new("status"));
            Assert.True(status.Success); Assert.Equal("Running", status.Status!.State);
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync());
            var settings = (await client.SendAsync(new("settings"))).Settings!;
            settings.Dicom.Port = TestFixture.FreePort();
            var saved = await client.SendAsync(new("save", settings));
            Assert.True(saved.Success);
            Assert.Contains(settings.Dicom.Port.ToString(), await File.ReadAllTextAsync(SettingsStore.FilePath));
            var after = (await client.SendAsync(new("status"))).Status!;
            Assert.True(after.RestartRequired);
            Assert.Equal(fixture.Options.Port, after.ActiveConfiguration.Port);
            Assert.NotEqual(settings.Dicom.Port, after.ActiveConfiguration.Port);
            Assert.False((await client.SendAsync(new("arbitrary-command"))).Success);
            settings.Dicom.Port = 0;
            Assert.False((await client.SendAsync(new("save", settings))).Success);
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync());
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(AgentPaths.SocketPath);
                Assert.Equal(UnixFileMode.None, mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
                Assert.True(mode.HasFlag(UnixFileMode.GroupRead | UnixFileMode.GroupWrite));
            }
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public async Task StatusStillWorksWithHistoryDisabled()
    {
        await using var fixture = new TestFixture(v => v["Agent:EnableFileHistory"] = "false");
        await fixture.Receiver.StartAsync(default);
        using var server = new ManagementServer(fixture.Provider.GetRequiredService<SettingsStore>(), fixture.Repository,
            fixture.Options, fixture.Provider.GetRequiredService<ReceiverState>(), fixture.Store, NullLogger<ManagementServer>.Instance);
        await server.StartAsync(default);
        try
        {
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync(TestFixture.File()));
            var client = new ManagementClient();
            var status = await client.SendAsync(new("status"));
            Assert.True(status.Success); Assert.Equal(1, status.Status!.TotalReceivedFiles);
            Assert.Empty((await client.SendAsync(new("history"))).History!);
        }
        finally { await server.StopAsync(default); }
    }
}
