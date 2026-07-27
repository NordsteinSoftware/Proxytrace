namespace Proxytrace.Api.Auth;

/// <summary>
/// Operator-facing options for the local-mode session cookie (config section
/// <c>Authentication:SessionCookie</c>).
/// </summary>
public sealed class SessionCookieOptions
{
    /// <summary>
    /// Whether the session cookie carries the <c>Secure</c> attribute. This is deliberately NOT
    /// inferred from the request: the documented topology terminates TLS at a reverse proxy and
    /// forwards plain HTTP to the API, so <c>Request.IsHttps</c> is <see langword="false"/> on
    /// every hop and would strip <c>Secure</c> from an HTTPS installation's cookie. Defaults to
    /// <see langword="true"/> everywhere except the Development environment; set it to
    /// <see langword="false"/> only for a deliberate plain-HTTP deployment on a non-localhost host.
    /// </summary>
    public bool Secure { get; init; } = true;
}

/// <summary>
/// Issues and clears the local-mode session cookie.
/// </summary>
public interface ISessionCookie
{
    /// <summary>
    /// Writes the session JWT as the httpOnly session cookie.
    /// </summary>
    void Append(HttpResponse response, string token, DateTimeOffset expiresAt);

    /// <summary>
    /// Clears the session cookie (logout).
    /// </summary>
    void Delete(HttpResponse response);
}

/// <summary>
/// The local-mode session cookie. The session JWT is issued as an httpOnly cookie so the
/// SPA never has to persist it in script-readable storage (localStorage XSS hardening);
/// <c>SameSite=Strict</c> plus the API's JSON-only request bodies cover CSRF. The token is
/// also returned in the response body for non-browser API clients, which keep using the
/// Authorization header. <see cref="JwtBearerEventsFactory"/> falls back to this cookie
/// when no bearer token is present.
/// </summary>
internal sealed class SessionCookie : ISessionCookie
{
    public const string Name = "proxytrace_session";

    private readonly SessionCookieOptions options;

    public SessionCookie(SessionCookieOptions options)
    {
        this.options = options;
    }

    public void Append(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.Secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expiresAt,
        });

    public void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.Secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
}
