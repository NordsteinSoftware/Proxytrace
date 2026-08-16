namespace Proxytrace.Api.Middleware;

/// <summary>
/// Content-Security-Policy values applied by <see cref="SecurityHeadersMiddleware"/>.
/// Defaults mirror frontend/index.html's build-time meta; ops may override per environment.
/// </summary>
public sealed record SecurityHeadersOptions
{
    /// <summary>
    /// Gets or sets the content security policy.
    /// </summary>
    public string ContentSecurityPolicy { get; init; } =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self' https:; " +
        "base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// Gets or sets the docs content security policy.
    /// </summary>
    public string DocsContentSecurityPolicy { get; init; } =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'self' https:; " +
        "base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// Validates.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ContentSecurityPolicy))
        {
            throw new InvalidOperationException(
                $"{nameof(SecurityHeadersOptions)}.{nameof(ContentSecurityPolicy)} must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(DocsContentSecurityPolicy))
        {
            throw new InvalidOperationException(
                $"{nameof(SecurityHeadersOptions)}.{nameof(DocsContentSecurityPolicy)} must not be empty.");
        }
    }
}
