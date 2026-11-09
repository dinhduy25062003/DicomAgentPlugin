using System.Net;
using System.Net.Sockets;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Storage;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace DicomAgent.IntegrationTests;
public sealed class ReceiverTests
{
    [Fact]
    public async Task CEchoAndGracefulShutdown()
    {
        await using var f = new TestFixture(); await f.StartAsync();
        Assert.Equal(DicomStatus.Success, await f.SendAsync());
        await f.Receiver.StopAsync(default); Assert.False(f.Receiver.Listening);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CStorePreservesPixelsSyntaxMetadataAndHistory(bool implicitVr)
    {
        await using var f = new TestFixture(); await f.StartAsync();
        var syntax = implicitVr ? DicomTransferSyntax.ImplicitVRLittleEndian : DicomTransferSyntax.ExplicitVRLittleEndian;
        var file = TestFixture.File(syntax: syntax);
        Assert.Equal(DicomStatus.Success, await f.SendAsync(file));
        var history = await f.Repository.RecentAsync(default); var row = Assert.Single(history);
        Assert.Equal("DX", row.Modality); Assert.True(System.IO.File.Exists(row.FilePath));
        Assert.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd"), row.FilePath);
        var saved = await DicomFile.OpenAsync(row.FilePath);
        Assert.Equal(syntax, saved.Dataset.InternalTransferSyntax);
        Assert.Equal(DicomPixelData.Create(file.Dataset).GetFrame(0).Data, DicomPixelData.Create(saved.Dataset).GetFrame(0).Data);
        Assert.Equal(file.Dataset.GetSingleValue<string>(DicomTag.PatientName), saved.Dataset.GetSingleValue<string>(DicomTag.PatientName));
        Assert.Equal(row.SopInstanceUid, saved.Dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID));
        Assert.Equal(row.FileSize, new FileInfo(row.FilePath).Length);
    }
    [Fact]
    public async Task DuplicateAndConflictNeverOverwrite()
    {
        await using var f = new TestFixture(); await f.StartAsync();
        var file = TestFixture.File(); Assert.Equal(DicomStatus.Success, await f.SendAsync(file));
        var original = Assert.Single(await f.Repository.RecentAsync(default));
        var hash = await InstanceStore.HashAsync(original.FilePath, default);
        Assert.Equal(DicomStatus.Success, await f.SendAsync(file));
        Assert.Single(await f.Repository.RecentAsync(default));
        Assert.Equal(DicomStatus.Success, await f.SendAsync(file, calling: "SECOND_SCU"));
        var changed = TestFixture.File(file.Dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID), pixel: 99);
        Assert.NotEqual(DicomStatus.Success, await f.SendAsync(changed));
        Assert.Equal(hash, await InstanceStore.HashAsync(original.FilePath, default));
        Assert.Single(Directory.GetFiles(f.Store.Root, "*.dcm", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(f.Store.Root, "*.partial", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task ConcurrentCStoreSameAndDistinctInstances()
    {
        await using var f = new TestFixture(); await f.StartAsync();
        var shared = TestFixture.File();
        var files = Enumerable.Range(0, 6).Select(i => i < 3 ? TestFixture.File(shared.Dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID)) : TestFixture.File()).ToArray();
        var responses = await Task.WhenAll(files.Select(file => f.SendAsync(file)));
        Assert.All(responses, r => Assert.Equal(DicomStatus.Success, r));
        Assert.Equal(4, (await f.Repository.SummaryAsync(default)).Count);
        Assert.Equal(4, Directory.GetFiles(f.Store.Root, "*.dcm", SearchOption.AllDirectories).Length);
    }
    [Theory]
    [InlineData("WRONG", "TEST_SCU", null, null)]
    [InlineData("DICOM_AGENT", "BAD", "TEST_SCU", null)]
    [InlineData("DICOM_AGENT", "TEST_SCU", null, "192.0.2.1")]
    public async Task AssociationAuthorization(string called, string calling, string? allowAe, string? allowIp)
    {
        await using var f = new TestFixture(v => { if (allowAe is not null) v["Dicom:AllowedCallingAeTitles:0"] = allowAe; if (allowIp is not null) v["Dicom:AllowedRemoteAddresses:0"] = allowIp; });
        await f.StartAsync(); await Assert.ThrowsAnyAsync<Exception>(() => f.SendAsync(called: called, calling: calling));
    }
    [Fact]
    public async Task PortInUseFailsInsteadOfSpinning()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var f = new TestFixture(v => v["Dicom:Port"] = port.ToString());
        await Assert.ThrowsAsync<IOException>(() => f.Receiver.StartAsync(default));
    }
    [Fact]
    public async Task FileWriteFailureReturnsFailureAndNoHistory()
    {
        await using var f = new TestFixture(); await f.StartAsync();
        await System.IO.File.WriteAllTextAsync(f.Store.Root, "path is a file");
        Assert.NotEqual(DicomStatus.Success, await f.SendAsync(TestFixture.File()));
        Assert.Empty(await f.Repository.RecentAsync(default));
    }
    [Fact]
    public async Task MissingRequiredTagsRejectedAndOptionalTagsNullable()
    {
        var file = TestFixture.File(); file.Dataset.Remove(DicomTag.PatientName); file.Dataset.Remove(DicomTag.PatientID);
        Assert.Null(MetadataExtractor.Extract(file).PatientName);
        Assert.Null(MetadataExtractor.Extract(file).StudyDate);
        file.Dataset.Remove(DicomTag.StudyInstanceUID);
        Assert.Throws<InvalidDataException>(() => MetadataExtractor.Extract(file));
        await using var f = new TestFixture(); await f.StartAsync();
        Assert.NotEqual(DicomStatus.Success, await f.SendAsync(file));
    }
    [Fact]
    public async Task HashMatchesKnownSha256()
    {
        await using var f = new TestFixture(); var path = Path.Combine(f.Data, "hash.bin");
        await System.IO.File.WriteAllTextAsync(path, "abc");
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", await InstanceStore.HashAsync(path, default));
    }
    [Fact]
    public async Task SQLiteFailureRetainsFileAndReconciliationRepairsHistory()
    {
        await using var f = new TestFixture(); await f.Repository.InitializeAsync(default);
        var failing = new FailingRepository(f.Repository);
        var store = new InstanceStore(failing, f.Options, new(), Microsoft.Extensions.Logging.Abstractions.NullLogger<InstanceStore>.Instance, new());
        var result = await store.SaveAsync(TestFixture.File(), default);
        Assert.True(result.HistoryPending); Assert.True(System.IO.File.Exists(result.Path)); Assert.Empty(await f.Repository.RecentAsync(default));
        failing.Fail = false; await store.ReconcileAsync(default); await store.ReconcileAsync(default);
        Assert.Single(await f.Repository.RecentAsync(default));
    }
    [Fact]
    public async Task ActualSqliteSchemaFailureRetainsNetworkReceivedFile()
    {
        await using var f = new TestFixture(); await f.StartAsync();
        await using (var db = await f.Provider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<DicomAgent.Infrastructure.Persistence.AgentDbContext>>().CreateDbContextAsync())
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(db.Database, "DROP TABLE Instances");
        Assert.Equal(DicomStatus.Success, await f.SendAsync(TestFixture.File()));
        Assert.Single(Directory.GetFiles(f.Store.Root, "*.dcm", SearchOption.AllDirectories));
        Assert.Contains(f.Provider.GetRequiredService<ReceiverState>().Errors, x => x.Contains("history pending"));
    }
    [Fact]
    public async Task SizeLimitRejectsWithoutPublishingFile()
    {
        await using var f = new TestFixture(v => v["Dicom:MaxInstanceBytes"] = "1024"); await f.StartAsync();
        var file = TestFixture.File(); file.Dataset.AddOrUpdate(new DicomOtherByte(DicomTag.PixelData, new byte[4096]));
        try { Assert.NotEqual(DicomStatus.Success, await f.SendAsync(file)); } catch (DicomNetworkException) { }
        Assert.Empty(await f.Repository.RecentAsync(default));
        if (Directory.Exists(f.Store.Root)) Assert.Empty(Directory.GetFiles(f.Store.Root, "*.dcm", SearchOption.AllDirectories));
    }
}
