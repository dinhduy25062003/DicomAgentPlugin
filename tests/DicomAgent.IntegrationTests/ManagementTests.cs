using DicomAgent.Application.Models;
using System.Net;
using System.Net.Http.Json;
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
    public async Task LocalHttpSavesSettingsAndClientDisconnectDoesNotStopReceiver()
    {
        await using var fixture = new TestFixture();
        await fixture.StartAsync();
        using var server = fixture.Api();
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
            Assert.StartsWith("http://127.0.0.1:", (await ManagementClient.ConnectionAsync()).BaseUrl);
            Assert.False(File.Exists(Path.Combine(fixture.Data, "management.sock")));

        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public async Task DesktopHttpRequiresLocalKeyAndRejectsBrowserOrigins()
    {
        await using var fixture = new TestFixture(); using var api = fixture.Api(); await api.StartAsync(default);
        try
        {
            var connection = await ManagementClient.ConnectionAsync(); using var client = new HttpClient { BaseAddress = new Uri(connection.BaseUrl) };
            using var anonymous = await client.PostAsJsonAsync("/api/management/commands", new ManagementRequest("captures"));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var browser = new HttpRequestMessage(HttpMethod.Post, "/api/management/commands") { Content = JsonContent.Create(new ManagementRequest("captures")) };
            browser.Headers.Add(ManagementAccess.Header, connection.Key); browser.Headers.Add("Origin", "https://demo.tdental.vn");
            using var denied = await client.SendAsync(browser); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.True((await new ManagementClient().SendAsync(new("captures"))).Success);
        }
        finally { await api.StopAsync(default); }
    }

    [Fact]
    public async Task StatusStillWorksWithHistoryDisabled()
    {
        await using var fixture = new TestFixture(v => v["Agent:EnableFileHistory"] = "false");
        await fixture.Receiver.StartAsync(default);
        using var server = fixture.Api();
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
