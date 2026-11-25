using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DicomAgent.Application.Models;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Exceptions;
using DicomAgent.Host.Api;
using DicomAgent.Host.Workers;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure.Capture;
using DicomAgent.Infrastructure.Configuration;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class CaptureTests
{
    private static readonly CaptureInput Input = new("crm.service", Guid.NewGuid(), "Phiếu chụp X-quang", "Điều trị", "Panorama", "123", "Khách thử nghiệm");
    private const string Token = "Bearer synthetic-web-token-never-real";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task BrowserRequestBindsOneTenantAndSurvivesDeletionAndRestart()
    {
        await using var fixture = new TestFixture();
        var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>();
        using var api = fixture.Api();
        await api.StartAsync(default);
        using var client = await ClientAsync(fixture.Provider.GetRequiredService<LocalApiOptions>().Port);
        try
        {
            var id = Guid.NewGuid();
            using var post = Request(HttpMethod.Post, "/api/capture-requests", id, Input);
            var numericInput = JsonNode.Parse(JsonSerializer.Serialize(Input, Json))!; numericInput["partnerId"] = 123;
            post.Content = JsonContent.Create(numericInput);
            using var response = await client.SendAsync(post);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Token, json); Assert.DoesNotContain("protectedAuthorization", json);
            var request = JsonSerializer.Deserialize<CaptureRequest>(json, Json)!;
            Assert.Equal(id, request.Id); Assert.Equal("new", request.Status);
            Assert.Equal("demo", (await captures.SnapshotAsync(default)).TenantName);
            using var retry = await client.SendAsync(Request(HttpMethod.Post, "/api/capture-requests", id, Input));
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.Single((await captures.SnapshotAsync(default)).Requests);
            using var duplicate = await client.SendAsync(Request(HttpMethod.Post, "/api/capture-requests", input: Input with { EntityName = "Different display name", ServiceName = "Another service" }));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Contains("resModel, resId and PartnerId", await duplicate.Content.ReadAsStringAsync());
            using var conflicting = await client.SendAsync(Request(HttpMethod.Post, "/api/capture-requests", id, Input with { PartnerName = "Other" }));
            Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
            using var otherTenant = await client.SendAsync(Request(HttpMethod.Post, "/api/capture-requests", Guid.NewGuid(), Input, "other"));
            Assert.Equal(HttpStatusCode.Conflict, otherTenant.StatusCode);
            using var read = await client.SendAsync(Request(HttpMethod.Get, $"/api/capture-requests/{id}"));
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var wrongTokenRequest = Request(HttpMethod.Get, $"/api/capture-requests/{id}");
            wrongTokenRequest.Headers.Remove("Authorization"); wrongTokenRequest.Headers.Add("Authorization", "Bearer another");
            using var wrongToken = await client.SendAsync(wrongTokenRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
            var persistedPath = Path.Combine(fixture.Data, "requests", id.ToString("N") + ".json");
            Assert.DoesNotContain(Token, await File.ReadAllTextAsync(persistedPath));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(persistedPath));
            var reloaded = fixture.NewCaptureService();
            Assert.Equal(id, Assert.Single((await reloaded.SnapshotAsync(default)).Requests).Id);
            await captures.RemoveAsync(id, null, default);
            var afterDelete = fixture.NewCaptureService();
            Assert.Equal("demo", (await afterDelete.SnapshotAsync(default)).TenantName);
            await Assert.ThrowsAsync<CaptureConflictException>(() => afterDelete.ReceiveAsync(Guid.NewGuid(), "other", Token, Input, default));
        }
        finally { await api.StopAsync(default); }
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameBusinessKeyCreateOnlyOneStoredRequest()
    {
        await using var fixture = new TestFixture(); using var api = fixture.Api(); await api.StartAsync(default);
        using var client = await ClientAsync(fixture.Provider.GetRequiredService<LocalApiOptions>().Port);
        try
        {
            using var first = Request(HttpMethod.Post, "/api/capture-requests", Guid.NewGuid(), Input);
            using var second = Request(HttpMethod.Post, "/api/capture-requests", Guid.NewGuid(), Input with { PartnerName = "Changed display name" });
            var responses = await Task.WhenAll(client.SendAsync(first), client.SendAsync(second));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            }
            finally { foreach (var response in responses) response.Dispose(); }
            var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>();
            Assert.Single((await captures.SnapshotAsync(default)).Requests);
            Assert.Single(Directory.GetFiles(Path.Combine(fixture.Data, "requests"), "*.json"));
            var restarted = fixture.NewCaptureService();
            await Assert.ThrowsAsync<CaptureConflictException>(() => restarted.ReceiveAsync(Guid.NewGuid(), "demo", Token, Input, default));
        }
        finally { await api.StopAsync(default); }
    }

    [Fact]
    public async Task CompletedRequestAllowsANewHttpRequestWithTheSameBusinessKeyAfterReload()
    {
        await using var fixture = new TestFixture();
        var firstId = Guid.NewGuid(); var repository = fixture.Provider.GetRequiredService<ICaptureRequestRepository>();
        var secrets = fixture.Provider.GetRequiredService<IRequestSecretStore>();
        await repository.LoadAsync(default); await repository.BindTenantAsync("demo", default);
        await repository.SaveAsync(new(new(firstId, "demo", Input, DateTime.UtcNow, "done", [], CompletedAtUtc: DateTime.UtcNow), secrets.Protect(Token), "https://demo.tdental.vn/"), default);
        using var api = fixture.Api(); await api.StartAsync(default);
        using var client = await ClientAsync(fixture.Provider.GetRequiredService<LocalApiOptions>().Port);
        try
        {
            var nextId = Guid.NewGuid();
            using var created = await client.SendAsync(Request(HttpMethod.Post, "/api/capture-requests", nextId, Input));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var blocked = await client.SendAsync(Request(HttpMethod.Post, "/api/capture-requests", Guid.NewGuid(), Input));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var records = (await fixture.Provider.GetRequiredService<ICaptureRequestService>().SnapshotAsync(default)).Requests;
            Assert.Equal("done", records.Single(value => value.Id == firstId).Status);
            Assert.Equal("new", records.Single(value => value.Id == nextId).Status);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(fixture.Data, "requests"), "*.json").Length);
        }
        finally { await api.StopAsync(default); }
    }

    [Fact]
    public async Task LocalApiChecksCorsPreflightHostAuthorizationAndPayload()
    {
        await using var fixture = new TestFixture();
        var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>();
        var port = fixture.Provider.GetRequiredService<LocalApiOptions>().Port;
        using var api = fixture.Api();
        await api.StartAsync(default); using var client = await ClientAsync(port);
        try
        {
            using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/capture-requests");
            preflight.Headers.Add("Origin", "https://demo.tdental.vn");
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "Authorization,TenantName,Content-Type");
            using var allowed = await client.SendAsync(preflight);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
            Assert.Equal("https://demo.tdental.vn", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
            using var evil = Request(HttpMethod.Post, "/api/capture-requests", Guid.NewGuid(), Input);
            evil.Headers.Remove("Origin"); evil.Headers.Add("Origin", "https://demo.tdental.vn.evil.example");
            using var denied = await client.SendAsync(evil); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var rebinding = new HttpRequestMessage(HttpMethod.Get, "/api/health"); rebinding.Headers.Host = "evil.example:" + port;
            using var rebound = await client.SendAsync(rebinding); Assert.Equal(HttpStatusCode.Forbidden, rebound.StatusCode);
            using var anonymous = await client.PostAsJsonAsync("/api/capture-requests", Input); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var invalid = await client.SendAsync(Request(HttpMethod.Post, "/api/capture-requests", Guid.NewGuid(), Input with { ResId = Guid.Empty }));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var tenantOriginMismatch = Request(HttpMethod.Post, "/api/capture-requests", Guid.NewGuid(), Input);
            tenantOriginMismatch.Headers.Remove("Origin"); tenantOriginMismatch.Headers.Add("Origin", "https://other.tdental.vn");
            using var mismatch = await client.SendAsync(tenantOriginMismatch); Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);
            Assert.Empty((await captures.SnapshotAsync(default)).Requests);
        }
        finally { await api.StopAsync(default); }
    }

    [Fact]
    public async Task RealDicomAndIpcImportUploadCorrectMultipartAndIgnoreLateImages()
    {
        var port = TestFixture.FreePort();
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        await using var saas = builder.Build();
        var uploaded = new TaskCompletionSource<IFormCollection>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? authorization = null;
        var count = 0;
        saas.MapPost("/api/IrAttachments", async context =>
        {
            Interlocked.Increment(ref count);
            authorization = context.Request.Headers.Authorization;
            Assert.False(context.Request.Headers.ContainsKey("TenantName"));
            Assert.False(context.Request.Headers.ContainsKey("Idempotency-Key"));
            var form = await context.Request.ReadFormAsync();
            Assert.All(form.Files, x => Assert.Equal("application/dicom", x.ContentType));
            Assert.All(form.Files, x => Assert.True(x.Length > 0));
            uploaded.SetResult(form); context.Response.StatusCode = 200;
        });
        await saas.StartAsync();
        await using var fixture = new TestFixture(values => values["LocalApi:SaasBaseUrlTemplate"] = $"http://127.0.0.1:{port}");
        await fixture.StartAsync();
        var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>(); using var worker = new CaptureUploadWorker(captures); await worker.StartAsync(default);
        using var management = Management(fixture); await management.StartAsync(default);
        try
        {
            var id = Guid.NewGuid(); await captures.ReceiveAsync(id, "demo", Token, Input, default);
            // DICOM with no active request is retained without assignment.
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync(TestFixture.File()));
            Assert.Empty(Assert.Single((await captures.SnapshotAsync(default)).Requests).Files);
            await captures.ActivateAsync(id, default);
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync(TestFixture.File()));
            Assert.Single(Assert.Single((await captures.SnapshotAsync(default)).Requests).Files);
            var import = Path.Combine(fixture.Data, "selected.DCM");
            var largeFile = TestFixture.File();
            largeFile.Dataset.AddOrUpdate(FellowOakDicom.DicomTag.Rows, (ushort)256);
            largeFile.Dataset.AddOrUpdate(FellowOakDicom.DicomTag.Columns, (ushort)512);
            largeFile.Dataset.AddOrUpdate(new FellowOakDicom.DicomOtherByte(FellowOakDicom.DicomTag.PixelData, new byte[256 * 512]));
            await largeFile.SaveAsync(import);
            Assert.True(new FileInfo(import).Length > 65536);
            var client = new ManagementClient();
            Assert.True((await client.ImportAsync(id, import)).Success);
            var request = Assert.Single((await captures.SnapshotAsync(default)).Requests);
            Assert.Equal(2, request.Files.Length);
            var stale = await client.SendAsync(new("capture-done", RequestId: id, FileIds: [request.Files[0].Id]));
            Assert.False(stale.Success); Assert.Contains("Images changed", stale.Error);
            var preview = await client.SendAsync(new("capture-preview", RequestId: id, FileId: request.Files[0].Id));
            Assert.True(preview.Success, preview.Error); Assert.Equal(2, preview.Preview!.Width); Assert.Equal(16, preview.Preview.Bgra.Length);
            Assert.True((await client.SendAsync(new("capture-done", RequestId: id, FileIds: request.Files.Select(x => x.Id).ToArray()))).Success);
            var form = await uploaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitStatusAsync(captures, "done");
            Assert.Equal(Token, authorization);
            Assert.Equal(Input.ResId.ToString(), form["ResId"]); Assert.Equal(Input.ResModel, form["ResModel"]); Assert.Equal("demo", form["Attachments.Tenant"]);
            Assert.Equal(2, form.Files.Count); Assert.All(form.Files, x => Assert.Equal("Attachments.Files", x.Name));
            Assert.False((await client.SendAsync(new("capture-done", RequestId: id, FileIds: request.Files.Select(x => x.Id).ToArray()))).Success);
            Assert.Equal(FellowOakDicom.Network.DicomStatus.Success, await fixture.SendAsync(TestFixture.File()));
            Assert.Equal(2, Assert.Single((await captures.SnapshotAsync(default)).Requests).Files.Length);
            Assert.Equal(1, count);
            var restarted = fixture.NewCaptureService();
            var restored = Assert.Single((await restarted.SnapshotAsync(default)).Requests);
            Assert.Equal("done", restored.Status); Assert.Equal(2, restored.Files.Length); Assert.NotNull(restored.CompletedAtUtc);
        }
        finally { await management.StopAsync(default); await worker.StopAsync(default); await saas.StopAsync(); }
    }

    [Fact]
    public async Task EachRequestUploadsWithItsOwnAuthorizationAfterRestart()
    {
        var port = TestFixture.FreePort();
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        await using var saas = builder.Build();
        var received = System.Threading.Channels.Channel.CreateUnbounded<(Guid ResId, string Authorization)>();
        saas.MapPost("/api/IrAttachments", async context =>
        {
            Assert.False(context.Request.Headers.ContainsKey("TenantName"));
            Assert.False(context.Request.Headers.ContainsKey("Idempotency-Key"));
            var form = await context.Request.ReadFormAsync();
            Assert.Equal("demo", form["Attachments.Tenant"]);
            await received.Writer.WriteAsync((Guid.Parse(form["ResId"].ToString()), context.Request.Headers.Authorization.ToString()));
            context.Response.StatusCode = 200;
        });
        await saas.StartAsync();
        await using var fixture = new TestFixture(values => values["LocalApi:SaasBaseUrlTemplate"] = $"http://127.0.0.1:{port}");
        await fixture.Repository.InitializeAsync(default);
        var original = fixture.Provider.GetRequiredService<ICaptureRequestService>();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        const string firstToken = "Bearer synthetic-request-A-token";
        const string secondToken = "Bearer synthetic-request-B-token";
        await original.ReceiveAsync(first, "demo", firstToken, Input, default);
        // Receiving a later request must not replace the earlier request's upload credentials.
        var secondInput = Input with { ResId = Guid.NewGuid(), PartnerId = "456", PartnerName = "Another synthetic patient" };
        await original.ReceiveAsync(second, "demo", secondToken, secondInput, default);
        foreach (var id in new[] { first, second })
        {
            var file = TestFixture.File(); var stored = await fixture.Store.SaveAsync(file, default, associateCapture: false);
            await original.AttachAsync(id, DicomCaptureImageReader.Describe(file, stored.Path), false, default);
        }
        var restarted = fixture.NewCaptureService();
        using var restartWorker = new CaptureUploadWorker(restarted); await restartWorker.StartAsync(default);
        try
        {
            foreach (var expected in new[] { (Id: first, ResId: Input.ResId, Token: firstToken), (Id: second, ResId: secondInput.ResId, Token: secondToken) })
            {
                await restarted.QueueUploadAsync(expected.Id, default);
                var upload = await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(expected.ResId, upload.ResId); Assert.Equal(expected.Token, upload.Authorization);
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    if ((await restarted.SnapshotAsync(default)).Requests.Single(x => x.Id == expected.Id).Status == "done") break;
                    await Task.Delay(30);
                }
                Assert.Equal("done", (await restarted.SnapshotAsync(default)).Requests.Single(x => x.Id == expected.Id).Status);
            }
        }
        finally { await restartWorker.StopAsync(default); await saas.StopAsync(); }
    }

    [Theory]
    [InlineData(401, "new")]
    [InlineData(503, "error")]
    [InlineData(302, "error")]
    public async Task UploadFailuresKeepImagesAndRequireErrorResolutionForUncertainResults(int status, string expected)
    {
        var port = TestFixture.FreePort();
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        await using var saas = builder.Build(); var count = 0;
        saas.MapPost("/api/IrAttachments", async context =>
        {
            Interlocked.Increment(ref count); await context.Request.ReadFormAsync();
            context.Response.StatusCode = status; context.Response.Headers.Location = "/redirect-target";
        });
        saas.MapPost("/redirect-target", (HttpContext _) => Task.FromException(new InvalidOperationException("Redirect must not be followed.")));
        await saas.StartAsync();
        await using var fixture = new TestFixture(values => values["LocalApi:SaasBaseUrlTemplate"] = $"http://127.0.0.1:{port}");
        await fixture.Repository.InitializeAsync(default);
        var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>(); using var worker = new CaptureUploadWorker(captures); await worker.StartAsync(default);
        try
        {
            var id = Guid.NewGuid(); await captures.ReceiveAsync(id, "demo", Token, Input, default); await captures.ActivateAsync(id, default);
            await fixture.Store.SaveAsync(TestFixture.File(), default); await captures.QueueUploadAsync(id, default);
            await WaitStatusAsync(captures, expected);
            var request = Assert.Single((await captures.SnapshotAsync(default)).Requests);
            Assert.Null(request.CompletedAtUtc); Assert.NotNull(request.LastError); Assert.True(File.Exists(Assert.Single(request.Files).FilePath));
            if (expected == "error")
            {
                await Assert.ThrowsAsync<CaptureConflictException>(() => captures.QueueUploadAsync(id, default));
                await captures.ResolveAsync(id, false, default);
                Assert.Equal("new", Assert.Single((await captures.SnapshotAsync(default)).Requests).Status);
            }
            var renewed = await captures.ReceiveAsync(id, "demo", "Bearer renewed-token", Input, default);
            Assert.Single(renewed.Files); Assert.Equal(1, count);
        }
        finally { await worker.StopAsync(default); await saas.StopAsync(); }
    }

    [Fact]
    public async Task ActiveCaptureCannotSwitchAndInterruptedUploadIsRecoveredAsError()
    {
        await using var fixture = new TestFixture();
        var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await captures.ReceiveAsync(first, "demo", Token, Input, default); await captures.ReceiveAsync(second, "demo", Token, Input with { PartnerId = "456" }, default);
        await captures.ActivateAsync(first, default);
        await Assert.ThrowsAsync<CaptureConflictException>(() => captures.ActivateAsync(second, default));
        var oldSession = await captures.CurrentSessionAsync(default);
        await captures.ActivateAsync(null, default); await captures.ActivateAsync(second, default);
        await fixture.Repository.InitializeAsync(default);
        await fixture.Store.SaveAsync(TestFixture.File(), default, associateCapture: false, session: oldSession);
        Assert.All((await captures.SnapshotAsync(default)).Requests, x => Assert.Empty(x.Files));
        // Re-activating the same request must not accept data from its earlier association.
        await captures.ActivateAsync(null, default); await captures.ActivateAsync(first, default);
        await fixture.Store.SaveAsync(TestFixture.File(), default, associateCapture: false, session: oldSession);
        Assert.All((await captures.SnapshotAsync(default)).Requests, x => Assert.Empty(x.Files));
        await captures.ActivateAsync(null, default); await captures.ActivateAsync(second, default);
        await Assert.ThrowsAsync<CaptureConflictException>(() => captures.QueueUploadAsync(second, default));
        // Simulate an unclean process exit after publishing the uploading state.
        var path = Path.Combine(fixture.Data, "requests", first.ToString("N") + ".json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!; json["request"]!["status"] = "uploading";
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var restarted = fixture.NewCaptureService();
        var snapshot = await restarted.SnapshotAsync(default);
        Assert.Null(snapshot.ActiveRequestId); Assert.Equal("error", snapshot.Requests.Single(x => x.Id == first).Status);
        await restarted.ResolveAsync(first, true, default);
        Assert.Equal("done", (await restarted.SnapshotAsync(default)).Requests.Single(x => x.Id == first).Status);
    }

    [Fact]
    public async Task RequestListRemainsAvailableWhileAnotherClientIsImporting()
    {
        await using var fixture = new TestFixture();
        var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>(); var id = Guid.NewGuid();
        await captures.ReceiveAsync(id, "demo", Token, Input, default);
        using var management = Management(fixture); await management.StartAsync(default);
        try
        {
            var connection = await ManagementClient.ConnectionAsync();
            using var client = new HttpClient(); using var cancel = new CancellationTokenSource();
            var pipe = new System.IO.Pipelines.Pipe();
            using var content = new StreamContent(pipe.Reader.AsStream()); content.Headers.ContentLength = 100;
            using var message = new HttpRequestMessage(HttpMethod.Post, connection.BaseUrl + $"/api/management/captures/{id}/files") { Content = content };
            message.Headers.Add(ManagementAccess.Header, connection.Key);
            var pending = client.SendAsync(message, cancel.Token);
            await pipe.Writer.WriteAsync(new byte[] { 0 });
            await Task.Delay(50);
            var list = await new ManagementClient().SendAsync(new("captures")).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(list.Success); Assert.Equal(id, Assert.Single(list.Captures!.Requests).Id);
            cancel.Cancel(); await pipe.Writer.CompleteAsync();
            try { using var response = await pending; } catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException) { }

        }
        finally { await management.StopAsync(default); }
    }

    private static LocalApiServer Management(TestFixture fixture) => fixture.Api();
    private static HttpRequestMessage Request(HttpMethod method, string path, Guid? id = null, CaptureInput? input = null, string tenant = "demo")
    {
        var message = new HttpRequestMessage(method, path); message.Headers.Add("Origin", $"https://{tenant}.tdental.vn");
        message.Headers.Add("Authorization", Token); message.Headers.Add("TenantName", tenant);
        if (id is not null) message.Headers.Add("Idempotency-Key", id.ToString());
        if (input is not null) message.Content = JsonContent.Create(input);
        return message;
    }
    private static async Task<HttpClient> ClientAsync(int port)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try { using var response = await client.GetAsync("/api/health"); if (response.IsSuccessStatusCode) return client; }
            catch (HttpRequestException) { }
            await Task.Delay(20);
        }
        client.Dispose(); throw new TimeoutException("Local API did not start.");
    }
    private static async Task WaitStatusAsync(ICaptureRequestService captures, string status)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await captures.SnapshotAsync(default)).Requests.Single().Status == status) return;
            await Task.Delay(30);
        }
        Assert.Equal(status, (await captures.SnapshotAsync(default)).Requests.Single().Status);
    }
}
