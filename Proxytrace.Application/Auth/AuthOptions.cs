namespace Proxytrace.Application.Auth;

/// <summary>
/// Represents a auth options.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Gets or sets the oidc.
    /// </summary>
    public OidcOptions Oidc { get; init; } = new();
    /// <summary>
    /// Gets or sets the local.
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
    /// Provides additional functionality.
    /// </summary>
    public AuthMode Mode
        => string.IsNullOrWhiteSpace(Oidc.Authority)
            ? AuthMode.Local 
            : AuthMode.Oidc;

    /// <summary>
    /// Represents a oidc options.
    /// </summary>
    public sealed class OidcOptions
    {
        /// <summary>
        /// Gets or sets the authority.
        /// </summary>
        public string Authority { get; init; } = string.Empty;
        /// <summary>
        /// Gets or sets the audience.
        /// </summary>
        public string Audience { get; init; } = string.Empty;
        /// <summary>
        /// Gets or sets the require https metadata.
        /// </summary>
        public bool RequireHttpsMetadata { get; init; } = true;
        /// <summary>
        /// Gets or sets the email claim type.
        /// </summary>
        public string EmailClaimType { get; init; } = "email";
        /// <summary>
        /// Gets or sets the name claim type.
        /// </summary>
        public string NameClaimType { get; init; } = "name";
    }

    /// <summary>
    /// Represents a local section.
    /// </summary>
    public sealed class LocalSection
    {
        /// <summary>
        /// Gets or sets the signing key.
        /// </summary>
        public string SigningKey { get; init; } = string.Empty;
    }
}
