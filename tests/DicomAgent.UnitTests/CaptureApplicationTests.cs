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
