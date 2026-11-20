using DicomAgent.Application.Abstractions;
using Microsoft.Extensions.Hosting;

namespace DicomAgent.Host.Workers;

public sealed class CaptureUploadWorker(ICaptureRequestService captures) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) => await captures.RunUploadsAsync(stoppingToken);
}
