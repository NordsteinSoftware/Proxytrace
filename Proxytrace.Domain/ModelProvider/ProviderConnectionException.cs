namespace Proxytrace.Domain.ModelProvider;

/// <summary>
/// The exception that is thrown when a provider connection error occurs.
/// </summary>
public sealed class ProviderConnectionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderConnectionException"/> class.
    /// </summary>
    public ProviderConnectionException(ProviderConnectionError error, Exception innerException)
        : base($"Provider connection failed: {error}", innerException)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the error.
    /// </summary>
    public ProviderConnectionError Error { get; }
}
