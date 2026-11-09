using System.Net;
using System.Net.Sockets;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Storage;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace DicomAgent.IntegrationTests;

internal sealed class TestFixture : IAsyncDisposable
{
    public string Data { get; } = Path.Combine(Path.GetTempPath(), "dicom-test-" + Guid.NewGuid().ToString("N"));
    private readonly string? oldData = Environment.GetEnvironmentVariable("DICOM_AGENT_DATA");
    public ServiceProvider Provider { get; }
    public DicomOptions Options => Provider.GetRequiredService<DicomOptions>();
    public InstanceStore Store => Provider.GetRequiredService<InstanceStore>();
    public IInstanceRepository Repository => Provider.GetRequiredService<IInstanceRepository>();
    public ReceiverController Receiver => Provider.GetRequiredService<ReceiverController>();
    public TestFixture(Action<Dictionary<string, string?>>? configure = null, IInstanceRepository? repository = null)
    {
        Directory.CreateDirectory(Data); Environment.SetEnvironmentVariable("DICOM_AGENT_DATA", Data);
        var values = new Dictionary<string, string?>
        {
            ["Dicom:ListenAddress"] = "127.0.0.1",
            ["Dicom:Port"] = FreePort().ToString(),
            ["Dicom:StoragePath"] = Path.Combine(Data, "storage"),
            ["Dicom:AeTitle"] = "DICOM_AGENT"
        }; configure?.Invoke(values);
        var services = new ServiceCollection(); services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        services.AddAgent(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        if (repository is not null) services.AddSingleton(repository);
        Provider = services.BuildServiceProvider();
        DicomSetupBuilder.UseServiceProvider(Provider);
    }
    public static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    public async Task StartAsync() { await Repository.InitializeAsync(default); await Receiver.StartAsync(default); }
    public async Task<DicomStatus> SendAsync(DicomFile? file = null, string called = "DICOM_AGENT", string calling = "TEST_SCU")
    {
        var client = Provider.GetRequiredService<IDicomClientFactory>().Create("127.0.0.1", Options.Port, false, calling, called);
        DicomStatus? result = null;
        DicomRequest request;
        if (file is null) { var echo = new DicomCEchoRequest(); echo.OnResponseReceived += (_, r) => result = r.Status; request = echo; }
        else { var store = new DicomCStoreRequest(file); store.OnResponseReceived += (_, r) => result = r.Status; request = store; }
        await client.AddRequestAsync(request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.SendAsync(timeout.Token);
        return result ?? throw new InvalidOperationException("No DICOM response received.");
    }
    public static DicomFile File(string? sop = null, DicomTransferSyntax? syntax = null, byte pixel = 42)
    {
        var dataset = new DicomDataset(syntax ?? DicomTransferSyntax.ExplicitVRLittleEndian) {
            { DicomTag.SOPClassUID, DicomUID.DigitalXRayImageStorageForPresentation },
            { DicomTag.SOPInstanceUID, sop ?? DicomUID.Generate().UID },
            { DicomTag.StudyInstanceUID, "1.2.826.0.1.3680043.10.999.1" },
            { DicomTag.SeriesInstanceUID, "1.2.826.0.1.3680043.10.999.2" },
            { DicomTag.PatientID, "SYNTHETIC" }, { DicomTag.PatientName, "TEST^ONLY" },
            { DicomTag.Modality, "DX" }, { DicomTag.Rows, (ushort)2 }, { DicomTag.Columns, (ushort)2 },
            { DicomTag.BitsAllocated, (ushort)8 }, { DicomTag.BitsStored, (ushort)8 },
            { DicomTag.HighBit, (ushort)7 }, { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PhotometricInterpretation, "MONOCHROME2" }, { DicomTag.PixelRepresentation, (ushort)0 },
            new DicomOtherByte(DicomTag.PixelData, pixel, pixel, pixel, pixel)
        };
        return new DicomFile(dataset);
    }
    public async ValueTask DisposeAsync()
    {
        await Receiver.StopAsync(default); await Provider.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("DICOM_AGENT_DATA", oldData);
        Directory.Delete(Data, true);
    }
}
internal sealed class FailingRepository(IInstanceRepository inner) : IInstanceRepository
{
    public bool Fail { get; set; } = true;
    public Task<HashSet<string>> IndexedPathsAsync(string root, CancellationToken ct) => inner.IndexedPathsAsync(root, ct);
    public Task InitializeAsync(CancellationToken ct) => inner.InitializeAsync(ct);
    public Task UpsertAsync(DicomAgent.Domain.Entities.DicomInstance item, CancellationToken ct) => Fail ? throw new IOException("Simulated SQLite failure") : inner.UpsertAsync(item, ct);
    public Task<HistoryItem[]> RecentAsync(CancellationToken ct) => inner.RecentAsync(ct);
    public Task<(long Count, DateTime? Last)> SummaryAsync(CancellationToken ct) => inner.SummaryAsync(ct);
}
