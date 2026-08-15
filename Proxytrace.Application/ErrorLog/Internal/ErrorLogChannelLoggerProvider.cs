using Microsoft.Extensions.Logging;

namespace Proxytrace.Application.ErrorLog.Internal;

/// <summary>
/// Registers the <see cref="ErrorLogChannelLogger"/> with the logging pipeline so every category's
/// Error/Critical entries are captured into the <see cref="IErrorLogChannel"/>. Implements
/// <see cref="ISupportExternalScope"/> so captured entries can read the ambient logging scope —
/// used to pick up a caller-supplied <see cref="ErrorLogScope.ErrorIdKey"/> for deep-linking.
/// </summary>
internal sealed class ErrorLogChannelLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IErrorLogChannel channel;
    private IExternalScopeProvider? scopeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorLogChannelLoggerProvider"/> class.
    /// </summary>
    public ErrorLogChannelLoggerProvider(IErrorLogChannel channel)
    {
        this.channel = channel;
    }

    /// <summary>
    /// Sets the scope provider.
    /// </summary>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => this.scopeProvider = scopeProvider;

    // The logger reads the scope provider lazily: the logging framework calls SetScopeProvider
    // after providers are constructed, and loggers are cached, so a snapshot at creation would be null.
    /// <summary>
    /// Creates the logger.
    /// </summary>
    public ILogger CreateLogger(string categoryName) =>
        new ErrorLogChannelLogger(categoryName, channel, () => scopeProvider);

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
    }
}
