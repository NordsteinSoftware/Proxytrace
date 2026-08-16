using Proxytrace.Domain.Outliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Dto.OutlierSettings;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for outlier settings operations.
/// </summary>
[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/outlier-settings")]
public class OutlierSettingsController : ControllerBase
{
    private readonly IOutlierSettingsStore store;
    private readonly OutlierSettingsDtoMapper mapper;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutlierSettingsController"/> class.
    /// </summary>
    public OutlierSettingsController(
        IOutlierSettingsStore store,
        OutlierSettingsDtoMapper mapper,
        ILogger<Audit> audit)
    {
        this.store = store;
        this.mapper = mapper;
        this.audit = audit;
    }

    /// <summary>Returns the stored settings, or the active defaults when none have been saved.</summary>
    [HttpGet]
    public async Task<ActionResult<OutlierSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var settings = await store.GetAsync(cancellationToken) ?? OutlierSettings.Default;
        return mapper.ToDto(settings);
    }

    /// <summary>
    /// Updates.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<OutlierSettingsDto>> Update(
        [FromBody] UpdateOutlierSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SigmaMultiplier <= 0d)
            return BadRequest("SigmaMultiplier must be greater than 0.");
        if (request.MinSampleCount < 1)
            return BadRequest("MinSampleCount must be at least 1.");
        if (request.SampleWindow < 1)
            return BadRequest("SampleWindow must be at least 1.");

        var settings = new OutlierSettings(
            request.Enabled, request.SigmaMultiplier, request.MinSampleCount, request.SampleWindow);

        await store.SaveAsync(settings, cancellationToken);
        audit.LogAudit(AuditAction.OutlierSettingsUpdated, targetType: "OutlierSettings");
        return mapper.ToDto(settings);
    }
}
