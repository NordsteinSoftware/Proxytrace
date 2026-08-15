using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// Represents a local token issuer.
/// </summary>
public interface ILocalTokenIssuer
{
    LocalTokenResult Issue(IUser user);
}

/// <summary>
/// Encapsulates the result of a local token operation.
/// </summary>
public sealed record LocalTokenResult(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Represents a local auth options.
/// </summary>
public sealed class LocalAuthOptions
{
    /// <summary>
    /// The section name constant value.
    /// </summary>
    public const string SectionName = "Authentication:Local";
    /// <summary>
    /// Gets or sets the signing key.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the issuer.
    /// </summary>
    public string Issuer { get; init; } = "proxytrace-local";
    /// <summary>
    /// Gets or sets the audience.
    /// </summary>
    public string Audience { get; init; } = "proxytrace-api";
    /// <summary>
    /// Gets or sets the token lifetime.
    /// </summary>
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromDays(7);
}
