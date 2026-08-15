using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Common.Hosting;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// Anonymous endpoint that surfaces the runtime configuration the SPA needs before any user exists:
/// kiosk mode flag, interactive mode flag, application version, and the public ingestion proxy base
/// URL.
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
    /// Returns the anonymous configuration payload used by the SPA on startup: kiosk mode, whether
    /// interactive features are available, the application version, and the ingestion proxy base URL.
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
