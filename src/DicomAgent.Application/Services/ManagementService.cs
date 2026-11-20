using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
namespace DicomAgent.Application.Services;

public sealed class ManagementService(ICaptureRequestService captures, IAgentAdministration administration, ICaptureFileImporter importer)
{
    public long MaxImportBytes => administration.MaxImportBytes;
    public async Task<ManagementResponse> HandleAsync(ManagementRequest request, CancellationToken ct)
    {
        ManagementResponse response;
        switch (request.Operation)
        {
            case "capture-preview" when request.RequestId is not null && request.FileId is not null:
                response = new(true, Preview: await captures.PreviewAsync(request.RequestId.Value, request.FileId.Value, ct)); break;
            case "captures": response = new(true, Captures: await captures.SnapshotAsync(ct)); break;
            case "capture-start" when request.RequestId is not null:
                await captures.ActivateAsync(request.RequestId, ct); response = new(true); break;
            case "capture-stop":
                await captures.ActivateAsync(null, ct); response = new(true); break;
            case "capture-delete" when request.RequestId is not null:
                await captures.RemoveAsync(request.RequestId.Value, request.FileId, ct); response = new(true); break;
            case "capture-done" when request.RequestId is not null:
                await captures.QueueUploadAsync(request.RequestId.Value, ct, request.FileIds ?? throw new ArgumentException("Confirmed image IDs are required.")); response = new(true); break;
            case "capture-confirm-uploaded" when request.RequestId is not null:
                await captures.ResolveAsync(request.RequestId.Value, true, ct); response = new(true); break;
            case "capture-allow-retry" when request.RequestId is not null:
                await captures.ResolveAsync(request.RequestId.Value, false, ct); response = new(true); break;
            case "storage-check":
                await administration.CheckStorageAsync(ct); response = new(true); break;
            case "settings": response = new(true, Settings: administration.Settings); break;
            case "save" when request.Settings is not null:
                await administration.SaveAsync(request.Settings, ct); response = new(true); break;
            case "history": response = new(true, History: await administration.HistoryAsync(ct)); break;
            case "status": response = new(true, Status: await administration.StatusAsync(ct)); break;
            default: response = new(false, "Unknown management operation."); break;
        }
        return response;
    }
    public async Task<ManagementResponse> ImportAsync(Guid id, Stream source, long length, CancellationToken ct)
    {
        await importer.ImportAsync(id, source, length, ct); return new(true);
    }
}
