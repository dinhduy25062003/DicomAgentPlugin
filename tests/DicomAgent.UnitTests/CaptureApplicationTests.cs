using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Exceptions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using Xunit;

namespace DicomAgent.UnitTests;

// These workflows run using only Application contracts, without Infrastructure, a filesystem or HTTP.
public sealed class CaptureApplicationTests
{
    private static readonly CaptureInput Input = new("crm.service", Guid.NewGuid(), "X-ray", "Treatment", "Panorama", "123", "Synthetic patient");
    private static CapturedDicomFile Image(string sop) => new("image.dcm", "unused-local-path", 100, "SYNTHETIC", "TEST^ONLY", "1.2.3", sop);

    [Fact]
    public async Task ReloadedRequestsKeepTheirOwnTokensWhenTheLaterRequestUploadsFirst()
    {
        var repository = new MemoryRepository(); var client = new RecordingClient();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var service = Create(repository, client);
        await service.ReceiveAsync(first, "demo", "Bearer token-A", Input, default);
        await service.ReceiveAsync(second, "demo", "Bearer token-B", Input with { PartnerId = "456" }, default);
        await service.AttachAsync(first, Image("1.2.3.1"), false, default);
        await service.AttachAsync(second, Image("1.2.3.2"), false, default);
        var reloaded = Create(repository, client);
        await reloaded.SnapshotAsync(default);
        foreach (var id in new[] { second, first })
        {
            await reloaded.QueueUploadAsync(id, default);
            await reloaded.ProcessUploadAsync(id, default);
        }
        Assert.Collection(client.Uploads,
            value => { Assert.Equal(second, value.Id); Assert.Equal("Bearer token-B", value.Authorization); },
            value => { Assert.Equal(first, value.Id); Assert.Equal("Bearer token-A", value.Authorization); });
        Assert.All((await reloaded.SnapshotAsync(default)).Requests, request => Assert.Equal("done", request.Status));
        await Assert.ThrowsAsync<CaptureConflictException>(() => reloaded.ProcessUploadAsync(first, default));
        Assert.Equal(2, client.Uploads.Count);
    }

    [Fact]
    public async Task FailedPersistenceDoesNotPublishAnUnstoredRequestInMemory()
    {
        var repository = new MemoryRepository { FailSave = true }; var service = Create(repository, new RecordingClient());
        await Assert.ThrowsAsync<IOException>(() => service.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer token-A", Input, default));
        Assert.Empty((await service.SnapshotAsync(default)).Requests);
    }

    [Theory]
    [InlineData(401, true, "new")]
    [InlineData(503, true, "review")]
    [InlineData(null, false, "new")]
    [InlineData(null, true, "review")]
    public async Task UploadOutcomeControlsWhetherAnotherAttemptNeedsReview(int? httpStatus, bool sent, string expected)
    {
        var client = new RecordingClient { Result = new(httpStatus, sent) };
        var service = Create(new MemoryRepository(), client); var id = Guid.NewGuid();
        await service.ReceiveAsync(id, "demo", "Bearer token-A", Input, default);
        await service.AttachAsync(id, Image("1.2.3.1"), false, default);
        await service.QueueUploadAsync(id, default);
        await service.ProcessUploadAsync(id, default);
        var request = Assert.Single((await service.SnapshotAsync(default)).Requests);
        Assert.Equal(expected, request.Status); Assert.Single(request.Files); Assert.Null(request.CompletedAtUtc);
        if (expected == "review") await Assert.ThrowsAsync<CaptureConflictException>(() => service.QueueUploadAsync(id, default));
    }

    [Fact]
    public async Task NewBusinessKeyBlocksAnotherRequestRegardlessOfDisplayFields()
    {
        var repository = new MemoryRepository(); var originalId = Guid.NewGuid();
        await repository.BindTenantAsync("demo", default);
        await repository.SaveAsync(new(new(originalId, "demo", Input, DateTime.UtcNow, "new", []), "test-only:Bearer original", "https://demo.tdental.vn/"), default);
        var service = Create(repository, new RecordingClient());
        var duplicate = Input with { EntityName = "Another title", ModelName = "Another label", ServiceName = "Another service", PartnerName = "Changed name" };
        await Assert.ThrowsAsync<CaptureConflictException>(() => service.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer duplicate", duplicate, default));
        Assert.Equal(originalId, Assert.Single((await service.SnapshotAsync(default)).Requests).Id);
        Assert.Equal(originalId, (await service.ReadForWebAsync(originalId, "demo", "Bearer original", default)).Id);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("uploading")]
    [InlineData("review")]
    public async Task NonNewStoredRequestAllowsOneNewRequestForTheSameBusinessKey(string status)
    {
        var repository = new MemoryRepository(); var originalId = Guid.NewGuid(); var nextId = Guid.NewGuid();
        await repository.BindTenantAsync("demo", default);
        await repository.SaveAsync(new(new(originalId, "demo", Input, DateTime.UtcNow, status, []), "test-only:Bearer original", "https://demo.tdental.vn/"), default);
        var service = Create(repository, new RecordingClient());
        var next = await service.ReceiveAsync(nextId, "demo", "Bearer next", Input, default);
        Assert.Equal("new", next.Status); Assert.Equal(nextId, next.Id);
        Assert.Equal(2, (await service.SnapshotAsync(default)).Requests.Length);
        await Assert.ThrowsAsync<CaptureConflictException>(() => service.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer duplicate", Input, default));
        Assert.Equal(originalId, (await service.ReadForWebAsync(originalId, "demo", "Bearer original", default)).Id);
        Assert.Equal(nextId, (await service.ReadForWebAsync(nextId, "demo", "Bearer next", default)).Id);
    }

    [Theory]
    [InlineData("resModel")]
    [InlineData("resId")]
    [InlineData("PartnerId")]
    public async Task ChangingAnyBusinessKeyFieldAllowsAnotherRequest(string field)
    {
        var service = Create(new MemoryRepository(), new RecordingClient());
        await service.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer original", Input, default);
        var other = field switch
        {
            "resModel" => Input with { ResModel = "other.model" },
            "resId" => Input with { ResId = Guid.NewGuid() },
            _ => Input with { PartnerId = "456" }
        };
        await service.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer second", other, default);
        Assert.Equal(2, (await service.SnapshotAsync(default)).Requests.Length);
    }

    [Fact]
    public async Task SameRequestIdCanRenewTokenWithoutCreatingAnotherRequestOrLosingImages()
    {
        var client = new RecordingClient(); var service = Create(new MemoryRepository(), client); var id = Guid.NewGuid();
        await service.ReceiveAsync(id, "demo", "Bearer original", Input, default);
        await service.AttachAsync(id, Image("1.2.3.1"), false, default);
        await service.ReceiveAsync(id, "demo", "Bearer renewed", Input, default);
        Assert.Single(Assert.Single((await service.SnapshotAsync(default)).Requests).Files);
        await service.QueueUploadAsync(id, default); await service.ProcessUploadAsync(id, default);
        Assert.Equal("Bearer renewed", Assert.Single(client.Uploads).Authorization);
    }

    [Fact]
    public async Task DeletingStoredRequestAllowsItsBusinessKeyToBeCreatedAgainAfterReload()
    {
        var repository = new MemoryRepository(); var original = Create(repository, new RecordingClient()); var first = Guid.NewGuid();
        await original.ReceiveAsync(first, "demo", "Bearer original", Input, default);
        var restarted = Create(repository, new RecordingClient());
        await Assert.ThrowsAsync<CaptureConflictException>(() => restarted.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer duplicate", Input, default));
        await restarted.RemoveAsync(first, null, default);
        var afterDelete = Create(repository, new RecordingClient()); var next = Guid.NewGuid();
        await afterDelete.ReceiveAsync(next, "demo", "Bearer new", Input, default);
        Assert.Equal(next, Assert.Single((await afterDelete.SnapshotAsync(default)).Requests).Id);
    }

    [Fact]
    public async Task RestartDuringUploadParksTheRequestInReviewUntilTheOperatorChecksSaaS()
    {
        var repository = new MemoryRepository(); var id = Guid.NewGuid();
        await repository.BindTenantAsync("demo", default);
        await repository.SaveAsync(new(new(id, "demo", Input, DateTime.UtcNow, "uploading",
            [new(Guid.NewGuid(), "image.dcm", "/kept/path.dcm", 100, "SYNTHETIC", "TEST^ONLY", "1.2.3", "1.2.3.9", DateTime.UtcNow)]),
            "test-only:Bearer original", "https://demo.tdental.vn/"), default);
        var restarted = Create(repository, new RecordingClient());
        var snapshot = await restarted.SnapshotAsync(default);
        var request = Assert.Single(snapshot.Requests);
        Assert.Equal("review", request.Status);
        Assert.Contains("restarted", request.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Single(request.Files);
        // Review blocks a blind retry and deletion; the operator resolves it explicitly.
        await Assert.ThrowsAsync<CaptureConflictException>(() => restarted.QueueUploadAsync(id, default));
        await Assert.ThrowsAsync<CaptureConflictException>(() => restarted.RemoveAsync(id, null, default));
        await restarted.ResolveAsync(id, false, default);
        var after = Assert.Single((await restarted.SnapshotAsync(default)).Requests);
        Assert.Equal("new", after.Status);
        await restarted.QueueUploadAsync(id, default);
        await restarted.ProcessUploadAsync(id, default);
        Assert.Equal("done", Assert.Single((await restarted.SnapshotAsync(default)).Requests).Status);
    }

    [Fact]
    public async Task SameImageCannotAttachToTwoRequestsButRepeatedAttachIsIgnored()
    {
        var service = Create(new MemoryRepository(), new RecordingClient());
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await service.ReceiveAsync(first, "demo", "Bearer token-A", Input, default);
        await service.ReceiveAsync(second, "demo", "Bearer token-B", Input with { PartnerId = "456" }, default);
        await service.AttachAsync(first, Image("1.2.3.5"), false, default);
        await service.AttachAsync(first, Image("1.2.3.5"), false, default);
        await Assert.ThrowsAsync<CaptureConflictException>(() => service.AttachAsync(second, Image("1.2.3.5"), false, default));
        var requests = (await service.SnapshotAsync(default)).Requests;
        Assert.Single(requests.Single(x => x.Id == first).Files);
        Assert.Empty(requests.Single(x => x.Id == second).Files);
    }

    [Fact]
    public async Task ConcurrentReceiveForTheSameBusinessKeyCreatesExactlyOneRequest()
    {
        var repository = new MemoryRepository();
        var service = Create(repository, new RecordingClient());
        var attempts = Enumerable.Range(0, 8).Select(_ => (Id: Guid.NewGuid(), Authorization: "Bearer concurrent-" + Guid.NewGuid())).ToArray();
        var results = await Task.WhenAll(attempts.Select(async attempt =>
        {
            try { await service.ReceiveAsync(attempt.Id, "demo", attempt.Authorization, Input, default); return true; }
            catch (CaptureConflictException) { return false; }
        }));
        Assert.Single((await service.SnapshotAsync(default)).Requests);
        Assert.Equal(1, results.Count(success => success));
        Assert.Equal(attempts.Length - 1, results.Count(success => !success));
    }

    [Fact]
    public async Task CompletedRequestsRejectRemovingSingleImagesButCanBeDeleted()
    {
        var repository = new MemoryRepository(); var service = Create(repository, new RecordingClient()); var id = Guid.NewGuid();
        await service.ReceiveAsync(id, "demo", "Bearer token-A", Input, default);
        await service.AttachAsync(id, Image("1.2.3.7"), false, default);
        var fileId = Assert.Single((await service.SnapshotAsync(default)).Requests).Files.Single().Id;
        await service.QueueUploadAsync(id, default);
        await service.ProcessUploadAsync(id, default);
        Assert.Equal("done", Assert.Single((await service.SnapshotAsync(default)).Requests).Status);
        await Assert.ThrowsAsync<CaptureConflictException>(() => service.RemoveAsync(id, fileId, default));
        await service.RemoveAsync(id, null, default);
        Assert.Empty((await service.SnapshotAsync(default)).Requests);
    }

    private static CaptureRequestService Create(MemoryRepository repository, RecordingClient client) =>
        new(new(), repository, new TestSecrets(), client, new UnusedImageReader(), new MemoryQueue());
    private sealed class MemoryRepository : ICaptureRequestRepository
    {
        private readonly Dictionary<Guid, StoredCapture> records = [];
        private string? tenant;
        public bool FailSave { get; init; }
        public Task<CaptureRequestStore> LoadAsync(CancellationToken ct) => Task.FromResult(new CaptureRequestStore(tenant, records.Values.ToArray()));
        public Task BindTenantAsync(string value, CancellationToken ct) { tenant = value; return Task.CompletedTask; }
        public Task SaveAsync(StoredCapture value, CancellationToken ct)
        {
            if (FailSave) throw new IOException("Synthetic storage failure");
            records[value.Request.Id] = value; return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken ct) { records.Remove(id); return Task.CompletedTask; }
    }
    private sealed class RecordingClient : ISaasAttachmentClient
    {
        public List<(Guid Id, string Authorization)> Uploads { get; } = [];
        public AttachmentUploadResult Result { get; init; } = new(200, true);
        public Task<AttachmentUploadResult> UploadAsync(CaptureRequest request, string server, string authorization, CancellationToken ct)
        { Uploads.Add((request.Id, authorization)); return Task.FromResult(Result); }
    }
    private sealed class TestSecrets : IRequestSecretStore
    {
        public string Protect(string value) => "test-only:" + value;
        public string Unprotect(string value) => value["test-only:".Length..];
    }
    private sealed class UnusedImageReader : ICaptureImageReader
    {
        public Task<ImagePreview> PreviewAsync(string path, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class MemoryQueue : ICaptureUploadQueue
    {
        public bool TryEnqueue(Guid id) => true;
        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
