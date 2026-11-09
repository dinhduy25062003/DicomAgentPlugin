using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace DicomAgent.Host.Workers;
public sealed class DicomReceiverWorker(ReceiverController receiver, InstanceStore storage, IInstanceRepository repository,
    AgentOptions options, ReceiverState state, ILogger<DicomReceiverWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (options.EnableFileHistory)
            {
                try { await repository.InitializeAsync(stoppingToken); await storage.ReconcileAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { state.Error("History unavailable; received files will be reconciled later."); logger.LogError("History initialization failed ({ErrorType}).", ex.GetType().Name); }
            }
            if (options.EnableReceiver) await receiver.StartAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (options.EnableReceiver && !receiver.Listening) throw new IOException("DICOM listener stopped unexpectedly.");
                if (options.EnableFileHistory)
                {
                    try { await repository.InitializeAsync(stoppingToken); await storage.ReconcileAsync(stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { state.Error("History reconciliation pending."); logger.LogWarning("Reconciliation failed ({ErrorType}).", ex.GetType().Name); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        { Environment.ExitCode = 1; state.State = "Error"; state.Error("Receiver stopped; service restart required."); logger.LogCritical("Receiver failed ({ErrorType}).", ex.GetType().Name); throw; }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await receiver.StopAsync(shutdown.Token);
        }
    }
}
