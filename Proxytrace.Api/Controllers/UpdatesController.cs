using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Dto.Updates;
using Proxytrace.Application.Updates;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for updates operations.
/// </summary>
[ApiController]
[Route("api/updates")]
public class UpdatesController : ControllerBase
{
    private readonly IUpdateService updateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatesController"/> class.
    /// </summary>
    public UpdatesController(IUpdateService updateService)
    {
        this.updateService = updateService;
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public UpdateStatusDto Get()
    {
        var status = updateService.Current;
        return new UpdateStatusDto(
            CurrentVersion: status.CurrentVersion,
            LatestVersion: status.LatestVersion,
            UpdateAvailable: status.UpdateAvailable,
            ReleaseUrl: status.ReleaseUrl,
            CheckedAt: status.CheckedAt);
    }
}
