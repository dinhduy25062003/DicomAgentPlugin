using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Exceptions;
using DicomAgent.Application.Models;

namespace DicomAgent.Application.Services;

// Owns the local capture-request lifecycle: receiving web requests, attaching DICOM
// images, running uploads to the SaaS and recovering state after a restart.
//
// CaptureRequest.Status state machine:
//   new      -> uploading  (operator pressed Done with confirmed images)
//   uploading-> done       (SaaS answered 2xx)
//   uploading-> error      (uncertain outcome: operator must check SaaS attachments first)
//   uploading-> new        (definitive rejection: request never sent, or 401/403/404/413/415/429)
//   error    -> done | new (operator resolves after checking SaaS manually)
//
// All state transitions are serialized through `gate`. `records` is the in-memory
// mirror of the JSON repository; LoadAsync populates it once per service lifetime.
// Each request keeps its own Authorization header; tokens are stored protected
// (IRequestSecretStore) and never returned to callers or logs.
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

    // The installation binds to the tenant of the first request forever; later
    // requests from other tenants are rejected until the data directory is reset.
    private async Task BindTenantAsync(string tenant, CancellationToken ct)
    {
        await repository.BindTenantAsync(tenant, ct);
        boundTenant = tenant;
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
            var recovered = RecoverAfterRestart(value);
            if (recovered != value) await repository.SaveAsync(recovered, ct);
            // Older installs stored the failed-upload state as "review"; read it as
            // "error" without rewriting the file until the record is saved again.
            records[recovered.Request.Id] = recovered.Request.Status == "review"
                ? recovered with { Request = recovered.Request with { Status = "error" } }
                : recovered;
        }
        loaded = true;
    }

    // A crash during upload must not wedge the request in "uploading": park it in
    // "error" so the operator checks the SaaS before any further attempt.
    private static StoredCapture RecoverAfterRestart(StoredCapture value)
    {
        if (value.Request.Status != "uploading") return value;
        return value with { Request = value.Request with
        {
            Status = "error",
            LastError = "App restarted during upload. Check SaaS attachments before allowing another attempt."
        } };
    }

    private async Task PersistAsync(StoredCapture value, CancellationToken ct)
    {
        await repository.SaveAsync(value, ct);
        records[value.Request.Id] = value;
    }

    private StoredCapture Get(Guid id) => records.TryGetValue(id, out var value)
        ? value : throw new KeyNotFoundException("Request not found.");

    public async Task<CaptureSnapshot> SnapshotAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            var requests = records.Values.Select(x => x.Request).OrderByDescending(x => x.CreatedAtUtc).ToArray();
            return new(requests, active, boundTenant);
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureRequest> ReceiveAsync(Guid id, string tenant, string authorization, CaptureInput input, CancellationToken ct)
    {
        tenant = CaptureRules.Tenant(tenant);
        CaptureRules.Input(input);
        CaptureRules.Authorization(authorization);
        var server = CaptureRules.Server(options, tenant).AbsoluteUri;
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            if (boundTenant is not null && boundTenant != tenant)
                throw new CaptureConflictException("This app is already assigned to tenant " + boundTenant + ". Requests from another tenant are not accepted.");
            if (boundTenant is null)
                await BindTenantAsync(tenant, ct);
            // Same Idempotency-Key + same body renews the token; the request, its
            // images and its completion state stay untouched.
            if (records.TryGetValue(id, out var existing))
            {
                if (existing.Request.TenantName != tenant || existing.Request.Info != input || existing.ServerUrl != server)
                    throw new CaptureConflictException("Idempotency key already belongs to another request.");
                await PersistAsync(existing with { ProtectedAuthorization = secrets.Protect(authorization) }, ct);
                return existing.Request;
            }
            // Only an unfinished request ("new") with the same business key blocks a
            // new one; done/error/uploading requests do not.
            if (records.Values.Any(value => value.Request.Status == "new" && value.Request.Info.ResModel == input.ResModel &&
                value.Request.Info.ResId == input.ResId && value.Request.Info.PartnerId == input.PartnerId))
                throw new CaptureConflictException("A new request with the same resModel, resId and PartnerId already exists in local storage.");
            var value = new StoredCapture(new(id, tenant, input, DateTime.UtcNow, "new", []), secrets.Protect(authorization), server);
            await PersistAsync(value, ct);
            return value.Request;
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureRequest> ReadForWebAsync(Guid id, string tenant, string authorization, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            var value = Get(id);
            if (value.Request.TenantName != tenant.ToLowerInvariant() || secrets.Unprotect(value.ProtectedAuthorization) != authorization)
                throw new UnauthorizedAccessException();
            // The browser never receives local paths or patient metadata from received DICOM.
            var safe = value.Request.Files
                .Select(x => x with { FilePath = "", PatientId = null, PatientName = null })
                .ToArray();
            return value.Request with { Files = safe };
        }
        finally { gate.Release(); }
    }

    public async Task<CaptureSession?> CurrentSessionAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            return active is null ? null : new(active.Value, activeGeneration);
        }
        finally { gate.Release(); }
    }

    public async Task ActivateAsync(Guid? id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            if (id is not null)
            {
                if (Get(id.Value).Request.Status != "new")
                    throw new CaptureConflictException("Chỉ có thể thay đổi trên yêu cầu mới");
                if (active is not null && active != id)
                    throw new CaptureConflictException("Vui lòng dừng tiến trình nhận ảnh hiện tại để có thể thực hiện yêu cầu mới");
            }
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
            await LoadAsync(ct);
            var value = Get(id);
            var request = value.Request;
            // Silent no-op when the image belongs to an association captured before
            // the operator switched requests (stale generation or stopped session).
            if (requireActive && (request.Status != "new" || active != id ||
                (sessionGeneration is not null && sessionGeneration != activeGeneration)))
                return;
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
        try
        {
            await LoadAsync(ct);
            path = Get(id).Request.Files.Single(x => x.Id == fileId).FilePath;
        }
        finally { gate.Release(); }
        return await images.PreviewAsync(path, ct);
    }

    public async Task RemoveAsync(Guid id, Guid? fileId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await LoadAsync(ct);
            var value = Get(id);
            if (value.Request.Status is "uploading" or "error")
                throw new CaptureConflictException("Resolve upload status before deleting this request or its images.");
            if (fileId is not null)
            {
                if (value.Request.Status == "done") throw new CaptureConflictException("Completed requests are read-only.");
                var files = value.Request.Files.Where(x => x.Id != fileId).ToArray();
                await PersistAsync(value with { Request = value.Request with { Files = files } }, ct);
            }
            else
            {
                await repository.DeleteAsync(id, ct);
                records.Remove(id);
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
            await LoadAsync(ct);
            var value = Get(id);
            if (value.Request.Status != "new" || value.Request.Files.Length == 0)
                throw new CaptureConflictException("Select at least one image in a new request before pressing Done.");
            // expectedFileIds is the list the operator confirmed; new arrivals or
            // removals invalidate the confirmation.
            if (expectedFileIds is not null && !expectedFileIds.SequenceEqual(value.Request.Files.Select(x => x.Id)))
                throw new CaptureConflictException("Images changed since confirmation. Refresh and review all images again before Done.");
            if (records.Values.Any(x => x.Request.Status == "uploading"))
                throw new CaptureConflictException("Another upload is still running.");
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
            await LoadAsync(ct);
            var value = Get(id);
            if (value.Request.Status != "error") throw new CaptureConflictException("This request does not need review.");
            var request = value.Request with
            {
                Status = alreadyUploaded ? "done" : "new",
                CompletedAtUtc = alreadyUploaded ? DateTime.UtcNow : null,
                LastError = alreadyUploaded ? null : "Operator checked SaaS and allowed a new upload attempt."
            };
            await PersistAsync(value with { Request = request }, ct);
        }
        finally { gate.Release(); }
    }

    public async Task RunUploadsAsync(CancellationToken stoppingToken)
    {
        await SnapshotAsync(stoppingToken);
        await foreach (var id in uploads.ReadAllAsync(stoppingToken))
            await ProcessUploadAsync(id, stoppingToken);
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
        var result = await UploadWithGuardAsync(value, stoppingToken);
        await ApplyUploadOutcomeAsync(id, result);
    }

    private async Task<AttachmentUploadResult> UploadWithGuardAsync(StoredCapture value, CancellationToken stoppingToken)
    {
        var attempted = false;
        try
        {
            var authorization = secrets.Unprotect(value.ProtectedAuthorization);
            attempted = true;
            return await attachments.UploadAsync(value.Request, value.ServerUrl, authorization, stoppingToken);
        }
        catch (Exception)
        {
            // A failed adapter must never leave a request stuck in uploading or permit a blind retry.
            return new(null, attempted);
        }
    }

    private async Task ApplyUploadOutcomeAsync(Guid id, AttachmentUploadResult result)
    {
        var (status, error) = ClassifyUpload(result);
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            var latest = Get(id);
            var request = latest.Request with
            {
                Status = status,
                LastError = error,
                CompletedAtUtc = status == "done" ? DateTime.UtcNow : null
            };
            await PersistAsync(latest with { Request = request }, CancellationToken.None);
        }
        finally { gate.Release(); }
    }

    // Maps the raw upload outcome to the next request state and a safe operator
    // message. Definitive rejections go back to "new"; anything where the SaaS may
    // already have accepted files goes to "error" instead of retrying blindly.
    private static (string Status, string? Error) ClassifyUpload(AttachmentUploadResult result)
    {
        if (result.StatusCode is >= 200 and <= 299)
            return ("done", null);
        if (result.StatusCode is 401 or 403 or 404 or 413 or 415 or 429)
            return ("new", $"SaaS rejected upload (HTTP {result.StatusCode}). For expired credentials, re-send the same request from the web with its original Idempotency-Key.");
        if (result.StatusCode is not null)
            return ("error", $"SaaS returned HTTP {result.StatusCode}. Check attachments on SaaS before retrying; some files may have been accepted.");
        if (result.RequestSent)
            return ("error", "Upload result is uncertain. Check attachments on SaaS before allowing another attempt. Local images are retained.");
        return ("new", "Could not prepare upload. Check that all selected files and saved credentials are available. Local images are retained.");
    }
}
