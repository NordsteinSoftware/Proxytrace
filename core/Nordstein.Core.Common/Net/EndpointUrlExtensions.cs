namespace Nordstein.Core.Common.Net;

/// <summary>
/// Parsing for user-entered endpoint URLs (e.g. an upstream model provider's API base URL).
/// </summary>
public static class EndpointUrlExtensions
{
    /// <summary>
    /// Parses a user-entered endpoint URL into an absolute http(s) <see cref="Uri"/>, assuming
    /// <c>https://</c> when no scheme is given (e.g. "api.openai.com/v1" becomes
    /// "https://api.openai.com/v1"). Throws <see cref="MalformedEndpointUrlException"/> when the
    /// value is not a usable http(s) URL.
    /// </summary>
    public static Uri ToEndpointUri(this string value)
    {
        var trimmed = value.Trim();
        // Scheme presence is decided by the "://" separator rather than by Uri parsing, because
        // "localhost:5000" already parses as an absolute Uri with scheme "localhost".
        var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : $"https://{trimmed}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new MalformedEndpointUrlException(value);
        }

        return uri;
    }
}
