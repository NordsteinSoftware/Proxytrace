using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Common.Hosting;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for config operations.
/// </summary>
[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly KioskOptions kioskOptions;
    private readonly KioskEndpointOptions kioskEndpoint;
    private readonly IAppVersion appVersion;
    private readonly IngestionProxyOptions ingestionProxy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigController"/> class.
    /// </summary>
    public ConfigController(
        KioskOptions kioskOptions,
        KioskEndpointOptions kioskEndpoint,
        IAppVersion appVersion,
        IngestionProxyOptions ingestionProxy)
    {
        this.kioskOptions = kioskOptions;
        this.kioskEndpoint = kioskEndpoint;
        this.appVersion = appVersion;
        this.ingestionProxy = ingestionProxy;
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public object Get() => new
    {
        kiosk = kioskOptions.Enabled,

        // Interactive = full read-write. Always true outside kiosk; in kiosk only when a
        // real LLM endpoint is configured (unlocks runs, evaluations, proposals, CRUD).
        interactive = !kioskOptions.Enabled || kioskEndpoint.IsConfigured,

        // Anonymous version exposure is a conscious choice for a self-hosted product
        // (documented in the operator manual); the SPA shows it in the about/footer area.
        version = appVersion.Version,

        // The ingestion proxy runs as its own service (own host port / hostname), so the SPA
        // cannot derive its address from the page origin. Null when the operator didn't set it.
        proxyBaseUrl = ingestionProxy.PublicBaseUrl,
    };
}
