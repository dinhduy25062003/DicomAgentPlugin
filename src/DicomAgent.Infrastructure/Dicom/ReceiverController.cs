using DicomAgent.Application.Models;
using FellowOakDicom.Network;
namespace DicomAgent.Infrastructure.Dicom;
public sealed class ReceiverController(IDicomServerFactory factory, DicomOptions options, ReceiverState state) : IDisposable
{
    private IDicomServer? server;
    public bool Listening => server?.IsListening == true;
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            server = factory.Create<StorageScp>(options.ListenAddress, options.Port,
                configure: o => o.MaxClientsAllowed = options.MaxConcurrentAssociations);
            for (var i = 0; i < 100 && !server.IsListening && server.Exception is null; i++) await Task.Delay(50, ct);
            if (!server.IsListening) throw new IOException("DICOM listener could not start.", server.Exception);
            state.State = "Running";
        }
        catch (Exception ex)
        { state.State = "Error"; Dispose(); throw new IOException("DICOM listener could not start.", ex); }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        if (server is null) return;
        server.Stop();
        // Let in-flight associations finish before closing their streams.
        for (var i = 0; i < 150 && server.GetNumberOfConnectedClients() > 0; i++) await Task.Delay(100, ct);
        Dispose(); state.State = "Stopped";
    }
    public void Dispose() { server?.Dispose(); server = null; }
}
