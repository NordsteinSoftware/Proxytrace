using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// Issues signed JWT session tokens for locally-authenticated users.
/// </summary>
public interface ILocalTokenIssuer
{
    /// <summary>
    /// Mints a signed JWT for the given user, returning the token string and its expiry.
    /// </summary>
    LocalTokenResult Issue(IUser user);
}

/// <summary>
/// A freshly minted JWT and its expiry timestamp.
/// </summary>
public sealed record LocalTokenResult(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Configuration for the local JWT issuer, bound from the 'Authentication:Local' config section.
/// </summary>
public sealed class LocalAuthOptions
{
    /// <summary>
    /// Configuration section path where <see cref="LocalAuthOptions"/> is bound.
    /// </summary>
    public const string SectionName = "Authentication:Local";

    /// <summary>
    /// Hex-encoded HMAC-SHA256 key used to sign local session JWTs.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// JWT 'iss' claim value; identifies this Proxytrace installation as the token issuer.
    /// </summary>
    public string Issuer { get; init; } = "proxytrace-local";

    /// <summary>
    /// JWT 'aud' claim value; must match the bearer validation audience configured in the API.
    /// </summary>
    public string Audience { get; init; } = "proxytrace-api";

    /// <summary>
    /// How long issued tokens remain valid before the user must re-authenticate.
    /// </summary>
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromDays(7);
}
