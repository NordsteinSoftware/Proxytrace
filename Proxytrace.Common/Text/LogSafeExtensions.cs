namespace Proxytrace.Common.Text;

/// <summary>
/// Neutralises untrusted values before they are written to a log.
/// </summary>
public static class LogSafeExtensions
{
    /// <summary>
    /// Removes the carriage returns and line feeds from a value that came from the request, so it
    /// cannot forge additional entries in a line-oriented log sink.
    ///
    /// The request line itself cannot carry a raw newline, but percent-encoded ones survive URL
    /// decoding: a request for <c>/x%0D%0AINFO:%20admin%20logged%20in</c> reaches
    /// <c>Request.Path</c> as two lines, and a flat-file or console sink renders the second as a
    /// log entry of its own. Structured sinks that keep the value in a field are unaffected, but
    /// the sink is a deployment choice, so sanitise at the call site instead of assuming one.
    /// </summary>
    public static string ToSingleLogLine(this string? value) =>
        value is null
            ? string.Empty
            : value
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
}
