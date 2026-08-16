namespace Proxytrace.Application.Auth;

/// <summary>
/// Selects between local password-based authentication and OIDC-backed authentication.
/// </summary>
public enum AuthMode
{
    /// <summary>
    /// Authentication is handled by an external OIDC provider configured via <see cref="AuthOptions.OidcOptions"/>.
    /// </summary>
    Oidc,

    /// <summary>
    /// Authentication is handled by the built-in local password store and JWT issuer.
    /// </summary>
    Local,
}
