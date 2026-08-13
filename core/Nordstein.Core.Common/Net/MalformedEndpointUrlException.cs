namespace Nordstein.Core.Common.Net;

/// <summary>
/// Thrown when a user-entered endpoint URL cannot be parsed into an absolute http(s) URL.
/// </summary>
public class MalformedEndpointUrlException : FormatException
{
    public MalformedEndpointUrlException(string value)
        : base($"'{value}' is not a valid endpoint URL. Expected an absolute http(s) URL like https://api.openai.com/v1.")
    {
    }
}
