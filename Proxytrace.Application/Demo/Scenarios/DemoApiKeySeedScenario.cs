using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.Kiosk;

namespace Proxytrace.Application.Demo.Scenarios;

/// <summary>
/// Seeds the fixed demo ingestion API key for the kiosk showcase. When kiosk mode runs with a live
/// <c>Kiosk:Endpoint</c>, a sample chat client points its OpenAI SDK <c>baseURL</c> at the kiosk API
/// and authenticates with this key, so every call becomes a live trace. The key is a verify-only
/// credential — only its SHA-256 hash is stored (same path as an operator-minted key via the API's
/// CreateKey endpoint) — so the configured plaintext is hashed here at the creation site and never
/// persisted in the clear.
///
/// Seeded only when a live endpoint is configured: that is the sole mode in which the in-process
/// proxy route is mounted, and the key must attach to the real provider created from
/// <c>Kiosk:Endpoint</c> so the proxy resolves the live upstream from storage exactly like production.
/// </summary>
[UsedImplicitly]
internal sealed class DemoApiKeySeedScenario : IDemoScenario
{
    private readonly KioskOptions kiosk;
    private readonly KioskEndpointOptions kioskEndpoint;
    private readonly DemoSeedContext ctx;
    private readonly IApiKey.CreateNew createApiKey;
    private readonly IApiKeyRepository apiKeys;
    private readonly ILogger<DemoApiKeySeedScenario> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoApiKeySeedScenario"/> class.
    /// </summary>
    public DemoApiKeySeedScenario(
        KioskOptions kiosk,
        KioskEndpointOptions kioskEndpoint,
        DemoSeedContext ctx,
        IApiKey.CreateNew createApiKey,
        IApiKeyRepository apiKeys,
        ILogger<DemoApiKeySeedScenario> logger)
    {
        this.kiosk = kiosk;
        this.kioskEndpoint = kioskEndpoint;
        this.ctx = ctx;
        this.createApiKey = createApiKey;
        this.apiKeys = apiKeys;
        this.logger = logger;
    }

    // After CoreSeedScenario (0), which creates the project, the demo user and the live provider.
    /// <summary>
    /// Gets the order.
    /// </summary>
    public int Order => 5;

    /// <summary>
    /// Seeds asynchronously.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!kioskEndpoint.IsConfigured)
        {
            // No live upstream: the proxy route is not mounted and there is no real provider to attach.
            return;
        }

        var plaintext = kiosk.DemoApiKey;
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            logger.LogWarning(
                "Kiosk:DemoApiKey is empty; skipping demo ingestion key seeding. The showcase proxy "
                + "will have no key to authenticate the sample client.");
            return;
        }

        // Verify-only credential: store only the hash and a short display prefix, mirroring the
        // operator key-mint path (ModelProvidersController.CreateKey).
        var key = createApiKey(
            "Kiosk demo key",
            Sha256.HexHash(plaintext),
            plaintext.Length <= 16 ? plaintext : plaintext[..16],
            ctx.RequireProject(),
            ctx.RequireKioskLiveProvider(),
            ApiKeyScopes.Ingestion,
            ctx.RequireDemoUser());
        await apiKeys.AddAsync(key, cancellationToken);
    }
}
