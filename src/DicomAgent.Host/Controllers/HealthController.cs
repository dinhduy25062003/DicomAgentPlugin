using Microsoft.AspNetCore.Mvc;

namespace DicomAgent.Host.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { application = "DicomAgent", status = "ready" });
}
