using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace DicomAgent.Host.Controllers;

[ApiController]
[Route("api/capture-requests")]
public sealed class CaptureRequestsController(ICaptureRequestService captures, LocalApiOptions options) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CaptureRequest>> Create([FromBody] CaptureInput input, CancellationToken ct)
    {
        var (tenant, authorization) = Headers();
        var origin = Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase) &&
            !CaptureRules.Server(options, tenant).GetLeftPart(UriPartial.Authority).Equals(origin, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403);
        var key = Request.Headers["Idempotency-Key"].ToString();
        var id = key.Length == 0 ? Guid.NewGuid()
            : Guid.TryParse(key, out var parsed) && parsed != Guid.Empty ? parsed
            : throw new ArgumentException("Invalid idempotency key.");
        await captures.ReceiveAsync(id, tenant, authorization, input, ct);
        var request = await captures.ReadForWebAsync(id, tenant, authorization, ct);
        return Created($"/api/capture-requests/{id}", request);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CaptureRequest>> Get(Guid id, CancellationToken ct)
    {
        var (tenant, authorization) = Headers();
        return Ok(await captures.ReadForWebAsync(id, tenant, authorization, ct));
    }

    private (string Tenant, string Authorization) Headers()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)) throw new UnauthorizedAccessException();
        CaptureRules.Authorization(authorization);
        return (CaptureRules.Tenant(Request.Headers["TenantName"].ToString()), authorization);
    }
}
