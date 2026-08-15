namespace Proxytrace.Domain.Kiosk;

/// <summary>
/// Configuration options for kiosk.
/// </summary>
public sealed record KioskOptions
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool Enabled { get; init; }
    /// <summary>
    /// Gets or sets the demo user email.
    /// </summary>
    public string DemoUserEmail { get; init; } = "demo@proxytrace.dev";
    /// <summary>
    /// Gets or sets the demo user name.
    /// </summary>
    public string DemoUserName { get; init; } = "Demo Visitor";

    /// <summary>
    /// Fixed plaintext of the ingestion API key seeded for the demo "Showcase Project" when kiosk
    /// mode runs with a live <c>Kiosk:Endpoint</c>. A sample chat client points its OpenAI SDK
    /// <c>baseURL</c> at the kiosk API and authenticates with this key, so every call becomes a live
    /// trace. Seeded only when a live endpoint is configured; the key is stored hashed (verify-only),
    /// exactly like an operator-minted key.
    /// </summary>
    public string DemoApiKey { get; init; } = "pk-kiosk-demo";
}
