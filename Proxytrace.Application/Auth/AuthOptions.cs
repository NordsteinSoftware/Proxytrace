namespace Proxytrace.Application.Auth;

/// <summary>
/// Root configuration block for authentication, bound from the 'Authentication' config section.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>
    /// OIDC configuration block; a non-empty <see cref="OidcOptions.Authority"/> activates OIDC mode.
    /// </summary>
    public OidcOptions Oidc { get; init; } = new();

    /// <summary>
    /// Local auth configuration block, used when no OIDC authority is configured.
    /// </summary>
    public LocalSection Local { get; init; } = new();

    /// <summary>
    /// Emergency break-glass switch (default <see langword="false"/>). When email delivery is
    /// unavailable, the self-service password reset normally logs only a REDACTED warning — a
    /// non-reversible token hint and the expiry, never the live link. Set this to
    /// <see langword="true"/> to instead log the full one-time reset URL so a locked-out sole admin can
    /// recover when SMTP is down. Anyone able to read the operator log within the token's 1-hour TTL can
    /// then take over the account, so leave it off except while actively recovering. See docs/security.md.
    /// </summary>
    public bool EmergencyLogResetLink { get; init; }

    /// <summary>
    /// Computed auth mode: <see cref="AuthMode.Oidc"/> when an OIDC authority is configured, <see cref="AuthMode.Local"/> otherwise.
    /// </summary>
    public AuthMode Mode
        => string.IsNullOrWhiteSpace(Oidc.Authority)
            ? AuthMode.Local 
            : AuthMode.Oidc;

    /// <summary>
    /// OIDC-provider settings consumed by the API's JWT bearer middleware.
    /// </summary>
    public sealed class OidcOptions
    {
        /// <summary>
        /// OIDC authority URL; a non-empty value enables OIDC mode and is used for metadata discovery.
        /// </summary>
        public string Authority { get; init; } = string.Empty;

        /// <summary>
        /// Expected audience claim used to validate incoming OIDC tokens.
        /// </summary>
        public string Audience { get; init; } = string.Empty;

        /// <summary>
        /// Whether the OIDC metadata endpoint must use HTTPS; set to false only in local development.
        /// </summary>
        public bool RequireHttpsMetadata { get; init; } = true;

        /// <summary>
        /// Claim type the OIDC provider uses for the user's email address.
        /// </summary>
        public string EmailClaimType { get; init; } = "email";

        /// <summary>
        /// Claim type the OIDC provider uses for the user's display name.
        /// </summary>
        public string NameClaimType { get; init; } = "name";
    }

    /// <summary>
    /// Local-auth sub-section that mirrors <see cref="LocalAuthOptions"/> for hosts that bind the top-level 'Authentication' block.
    /// </summary>
    public sealed class LocalSection
    {
        /// <summary>
        /// Hex-encoded HMAC-SHA256 key used to sign local session JWTs.
        /// </summary>
        public string SigningKey { get; init; } = string.Empty;
    }
}
