using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
namespace DicomAgent.Host.Controllers;

[ApiController]
[Route("api/management")]
public sealed class ManagementController(ManagementService management) : ControllerBase
{
    [HttpPost("commands")]
    public Task<ActionResult<ManagementResponse>> Command([FromBody] ManagementRequest request, CancellationToken ct) =>
        RunAsync(() => management.HandleAsync(request, ct), ct);

    [HttpPost("captures/{id:guid}/files")]
    [RequestSizeLimit(2147483648)]
    public Task<ActionResult<ManagementResponse>> Import(Guid id, CancellationToken ct) => RunAsync(async () =>
    {
        var length = Request.ContentLength ?? throw new ArgumentException("File size is required.");
        if (length <= 0 || length > management.MaxImportBytes) throw new ArgumentException("File exceeds the configured instance size limit.");
        return await management.ImportAsync(id, Request.Body, length, ct);
    }, ct);

    private async Task<ActionResult<ManagementResponse>> RunAsync(Func<Task<ManagementResponse>> action, CancellationToken ct)
    {
        try { return Ok(await action()); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return Ok(new ManagementResponse(false, ex is CaptureConflictException or ArgumentException or KeyNotFoundException
                ? ex.Message : "Request failed. Check the DICOM file, settings, service permissions and local storage."));
        }
    }
}
