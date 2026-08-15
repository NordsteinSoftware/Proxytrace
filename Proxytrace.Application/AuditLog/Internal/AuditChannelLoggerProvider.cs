using Proxytrace.Domain.AuditLog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Proxytrace.Application.AuditLog.Internal;

/// <summary>
/// Registers the <see cref="AuditChannelLogger"/> with the logging pipeline, but only for the
/// dedicated audit category (<c>ILogger&lt;Audit&gt;</c>). Every other category gets a
/// <see cref="NullLogger"/>, so the capture check runs only for deliberate audit calls. The audit
/// logger reports <c>IsEnabled == true</c>, so it is not silenced by the app's log-level configuration.
/// </summary>
internal sealed class AuditChannelLoggerProvider : ILoggerProvider
{
    private static readonly string AuditCategory = typeof(Audit).FullName ?? typeof(Audit).Name;

    private readonly AuditChannelLogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditChannelLoggerProvider"/> class.
    /// </summary>
    public AuditChannelLoggerProvider(IAuditChannel channel, IAuditActorAccessor? actorAccessor)
    {
        logger = new AuditChannelLogger(channel, actorAccessor);
    }

    /// <summary>
    /// Creates the logger.
    /// </summary>
    public ILogger CreateLogger(string categoryName)
        => categoryName == AuditCategory ? logger : NullLogger.Instance;

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
    }
}
