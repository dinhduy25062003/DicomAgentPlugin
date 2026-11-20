using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Exceptions;
using DicomAgent.Application.Models;

namespace DicomAgent.Application.Services;

public sealed class CaptureRequestService(LocalApiOptions options, ICaptureRequestRepository repository,
    IRequestSecretStore secrets, ISaasAttachmentClient attachments, ICaptureImageReader images,
    ICaptureUploadQueue uploads) : ICaptureRequestService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, StoredCapture> records = [];
    private bool loaded;
    private Guid? active;
    private Guid activeGeneration;
    private string? boundTenant;

    private async Task BindTenantAsync(string tenant, CancellationToken ct)
    {
        await repository.BindTenantAsync(tenant, ct); boundTenant = tenant;
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        if (loaded) return;
        var stored = await repository.LoadAsync(ct);
        boundTenant = stored.TenantName is null ? null : CaptureRules.Tenant(stored.TenantName);
        foreach (var value in stored.Requests)
        {
            var tenant = CaptureRules.Tenant(value.Request.TenantName);
            if (boundTenant is null) await BindTenantAsync(tenant, ct);
            if (boundTenant != tenant) throw new InvalidDataException("Request storage contains mixed tenants.");
            var recovered = value.Request.Status == "uploading"
                ? value with { Request = value.Request with { Status = "review", LastError = "App restarted during upload. Check SaaS attachments before allowing another attempt." } }
                : value;
            if (recovered != value) await repository.SaveAsync(recovered, ct);
            records[recovered.Request.Id] = recovered;
        }
        loaded = true;
    }
    private async Task PersistAsync(StoredCapture value, CancellationToken ct)
    {
        await repository.SaveAsync(value, ct);
        records[value.Request.Id] = value;
    }
    private StoredCapture Get(Guid id) => records.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("Request not found.");
    public async Task<CaptureSnapshot> SnapshotAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await LoadAsync(ct); return new(records.Values.Select(x => x.Request).OrderByDescending(x => x.CreatedAtUtc).ToArray(), active, boundTenant); }
        finally { gate.Release(); }
    }
    public async Task<CaptureRequest> ReceiveAsync(Guid id, string tenant, string authorization, CaptureInput input, CancellationToken ct)
    {
        tenant = CaptureRules.Tenant(tenant); CaptureRules.Input(input); CaptureRules.Authorization(authorization);
        var server = CaptureRules.Server(options, tenant).AbsoluteUri;
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            if (boundTenant is not null && boundTenant != tenant)
                throw new CaptureConflictException("This app is already assigned to tenant " + boundTenant + ". Requests from another tenant are not accepted.");
            if (boundTenant is null)
                await BindTenantAsync(tenant, ct);
            if (records.TryGetValue(id, out var existing))
            {
                if (existing.Request.TenantName != tenant || existing.Request.Info != input || existing.ServerUrl != server)
                    throw new CaptureConflictException("Idempotency key already belongs to another request.");
                // Re-delivery renews a token without changing the request, its images or completion state.
                await PersistAsync(existing with { ProtectedAuthorization = secrets.Protect(authorization) }, ct);
                return existing.Request;
            }
            var value = new StoredCapture(new(id, tenant, input, DateTime.UtcNow, "new", []), secrets.Protect(authorization), server);
            await PersistAsync(value, ct); return value.Request;
        }
        finally { gate.Release(); }
    }
    public async Task<CaptureRequest> ReadForWebAsync(Guid id, string tenant, string authorization, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct); var value = Get(id);
            if (value.Request.TenantName != tenant.ToLowerInvariant() || secrets.Unprotect(value.ProtectedAuthorization) != authorization)
                throw new UnauthorizedAccessException();
            // Browser never receives local paths or patient metadata from received DICOM.
            return value.Request with { Files = value.Request.Files.Select(x => x with { FilePath = "", PatientId = null, PatientName = null }).ToArray() };
        }
        finally { gate.Release(); }
    }
    public async Task<CaptureSession?> CurrentSessionAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await LoadAsync(ct); return active is null ? null : new(active.Value, activeGeneration); }
        finally { gate.Release(); }
    }
    public async Task ActivateAsync(Guid? id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            if (id is not null && Get(id.Value).Request.Status != "new") throw new CaptureConflictException("Only a new request can receive images.");
            if (id is not null && active is not null && active != id) throw new CaptureConflictException("Stop the current capture before starting another request.");
            if (active != id) activeGeneration = Guid.NewGuid();
            active = id;
        }
        finally { gate.Release(); }
    }
    public async Task AttachAsync(Guid id, CapturedDicomFile file, bool requireActive, CancellationToken ct, Guid? sessionGeneration = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct); var value = Get(id); var request = value.Request;
            if (requireActive && (request.Status != "new" || active != id || (sessionGeneration is not null && sessionGeneration != activeGeneration))) return;
            if (request.Status != "new") throw new CaptureConflictException("Only new requests can accept images.");
            if (request.Files.Any(x => x.SopInstanceUid == file.SopInstanceUid)) return;
            if (records.Values.Any(x => x.Request.Id != id && x.Request.Files.Any(f => f.SopInstanceUid == file.SopInstanceUid)))
                throw new CaptureConflictException("This DICOM image is already attached to another request. Check the patient and study.");
            if (request.Files.Length >= 100) throw new CaptureConflictException("Maximum 100 images per request.");
            var item = new CaptureFile(Guid.NewGuid(), file.FileName, file.FilePath, file.FileSize,
                file.PatientId, file.PatientName, file.StudyInstanceUid, file.SopInstanceUid, DateTime.UtcNow);
            await PersistAsync(value with { Request = request with { Files = [.. request.Files, item], LastError = null } }, ct);
        }
        finally { gate.Release(); }
    }
    public async Task<ImagePreview> PreviewAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        string path;
        await gate.WaitAsync(ct);
        try { await LoadAsync(ct); path = Get(id).Request.Files.Single(x => x.Id == fileId).FilePath; }
        finally { gate.Release(); }
        return await images.PreviewAsync(path, ct);
    }
    public async Task RemoveAsync(Guid id, Guid? fileId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct); var value = Get(id);
            if (value.Request.Status is "uploading" or "review") throw new CaptureConflictException("Resolve upload status before deleting this request or its images.");
            if (fileId is not null)
            {
                if (value.Request.Status == "done") throw new CaptureConflictException("Completed requests are read-only.");
                await PersistAsync(value with { Request = value.Request with { Files = value.Request.Files.Where(x => x.Id != fileId).ToArray() } }, ct);
            }
            else
            {
                await repository.DeleteAsync(id, ct); records.Remove(id);
                if (active == id) active = null;
            }
        }
        finally { gate.Release(); }
    }
    public async Task QueueUploadAsync(Guid id, CancellationToken ct, Guid[]? expectedFileIds = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct); var value = Get(id);
            if (value.Request.Status != "new" || value.Request.Files.Length == 0)
                throw new CaptureConflictException("Select at least one image in a new request before pressing Done.");
            if (expectedFileIds is not null && !expectedFileIds.SequenceEqual(value.Request.Files.Select(x => x.Id)))
                throw new CaptureConflictException("Images changed since confirmation. Refresh and review all images again before Done.");
            if (records.Values.Any(x => x.Request.Status == "uploading")) throw new CaptureConflictException("Another upload is still running.");
            await PersistAsync(value with { Request = value.Request with { Status = "uploading", LastError = null } }, ct);
            if (active == id) active = null;
            if (!uploads.TryEnqueue(id)) throw new CaptureConflictException("Upload queue is busy.");
        }
        finally { gate.Release(); }
    }
    public async Task ResolveAsync(Guid id, bool alreadyUploaded, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct); var value = Get(id);
            if (value.Request.Status != "review") throw new CaptureConflictException("This request does not need review.");
            await PersistAsync(value with { Request = value.Request with { Status = alreadyUploaded ? "done" : "new",
                CompletedAtUtc = alreadyUploaded ? DateTime.UtcNow : null,
                LastError = alreadyUploaded ? null : "Operator checked SaaS and allowed a new upload attempt." } }, ct);
        }
        finally { gate.Release(); }
    }
    public async Task RunUploadsAsync(CancellationToken stoppingToken)
    {
        await SnapshotAsync(stoppingToken);
        await foreach (var id in uploads.ReadAllAsync(stoppingToken)) await ProcessUploadAsync(id, stoppingToken);
    }
    public async Task ProcessUploadAsync(Guid id, CancellationToken stoppingToken)
    {
        StoredCapture value;
        await gate.WaitAsync(stoppingToken);
        try
        {
            value = Get(id);
            if (value.Request.Status != "uploading") throw new CaptureConflictException("Request is not queued for upload.");
        }
        finally { gate.Release(); }
        AttachmentUploadResult result;
        var attempted = false;
        try
        {
            var authorization = secrets.Unprotect(value.ProtectedAuthorization);
            attempted = true;
            result = await attachments.UploadAsync(value.Request, value.ServerUrl, authorization, stoppingToken);
        }
        catch (Exception)
        {
            // A failed adapter must never leave a request stuck in uploading or permit a blind retry.
            result = new(null, attempted);
        }
        string status; string? error;
        if (result.StatusCode is >= 200 and <= 299) { status = "done"; error = null; }
        else if (result.StatusCode is 401 or 403 or 404 or 413 or 415 or 429)
        {
            status = "new";
            error = $"SaaS rejected upload (HTTP {result.StatusCode}). For expired credentials, re-send the same request from the web with its original Idempotency-Key.";
        }
        else if (result.StatusCode is not null)
        {
            status = "review";
            error = $"SaaS returned HTTP {result.StatusCode}. Check attachments on SaaS before retrying; some files may have been accepted.";
        }
        else
        {
            status = result.RequestSent ? "review" : "new";
            error = result.RequestSent
                ? "Upload result is uncertain. Check attachments on SaaS before allowing another attempt. Local images are retained."
                : "Could not prepare upload. Check that all selected files and saved credentials are available. Local images are retained.";
        }
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            var latest = Get(id);
            await PersistAsync(latest with { Request = latest.Request with { Status = status, LastError = error,
                CompletedAtUtc = status == "done" ? DateTime.UtcNow : null } }, CancellationToken.None);
        }
        finally { gate.Release(); }
    }
}
