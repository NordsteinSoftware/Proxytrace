namespace Proxytrace.Api.Middleware.Exceptions;

/// <summary>
/// The HTTP response shape an <see cref="IExceptionMapper"/> produces for an exception:
/// status code, the wire "type" discriminator, and any exception-specific payload fields.
/// <see cref="Message"/> replaces the raw exception message on the wire when set — use it
/// whenever the exception text may contain internals (SQL, schema names, file paths).
/// </summary>
internal sealed record ExceptionMapping
{
    /// <summary>
    /// Gets or sets the status code.
    /// </summary>
    public required int StatusCode { get; init; }
    /// <summary>
    /// Gets or sets the type name.
    /// </summary>
    public required string TypeName { get; init; }
    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string? Message { get; init; }
    /// <summary>
    /// Gets or sets the additional fields.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; init; }
}
