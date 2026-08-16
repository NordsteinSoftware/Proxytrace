using Microsoft.AspNetCore.Mvc;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for health operations.
/// </summary>
[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
