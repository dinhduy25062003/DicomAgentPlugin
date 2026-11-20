using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Application.Exceptions;
using DicomAgent.Infrastructure.Storage;
using DicomAgent.Infrastructure.Capture;
using FellowOakDicom;
namespace DicomAgent.Infrastructure.Management;

public sealed class CaptureFileImporter(DicomOptions active, InstanceStore storage, ICaptureRequestService captures) : ICaptureFileImporter
{
    public async Task ImportAsync(Guid id, Stream source, long length, CancellationToken ct)
    {
        if (length <= 0 || length > active.MaxInstanceBytes) throw new ArgumentException("File is empty or exceeds the configured maximum instance size.");
        var snapshot = await captures.SnapshotAsync(ct);
        if (!snapshot.Requests.Any(x => x.Id == id && x.Status == "new"))
            throw new CaptureConflictException("Choose a new request before importing DICOM files.");
        var directory = Path.Combine(AgentPaths.DataDirectory, "receive-temp"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".import");
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var remaining = length; var buffer = new byte[81920];
                while (remaining > 0)
                {
                    var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                    if (count == 0) throw new EndOfStreamException("Incomplete file import.");
                    await target.WriteAsync(buffer.AsMemory(0, count), ct); remaining -= count;
                }
            }
            var file = await DicomFile.OpenAsync(path, FileReadOption.ReadAll);
            var result = await storage.SaveAsync(file, ct, associateCapture: false);
            await captures.AttachAsync(id, DicomCaptureImageReader.Describe(file, result.Path), false, ct);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
