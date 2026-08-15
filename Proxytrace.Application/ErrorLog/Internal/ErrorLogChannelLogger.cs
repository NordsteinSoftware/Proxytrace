using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.ApplicationError;

namespace Proxytrace.Application.ErrorLog.Internal;

/// <summary>
/// Captures every <c>Error</c>/<c>Critical</c> log entry onto the <see cref="IErrorLogChannel"/>.
/// Categories under the error-log pipeline itself and EF Core are skipped: a failed DB write makes
/// EF (and our own writer/cleanup) log at Error level, which must not re-enter the channel and loop.
/// Cancellation exceptions (client disconnect, shutdown) are also skipped — they are expected, not faults.
/// </summary>
internal sealed class ErrorLogChannelLogger : ILogger
{
    private const string SelfCategoryPrefix = "Proxytrace.Application.ErrorLog";
    private const string EfCategoryPrefix = "Microsoft.EntityFrameworkCore";

    private readonly string category;
    private readonly IErrorLogChannel channel;
    private readonly Func<IExternalScopeProvider?>? scopeAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorLogChannelLogger"/> class.
    /// </summary>
    public ErrorLogChannelLogger(
        string category,
        IErrorLogChannel channel,
        Func<IExternalScopeProvider?>? scopeAccessor = null)
    {
        this.category = category;
        this.channel = channel;
        this.scopeAccessor = scopeAccessor;
    }

    /// <summary>
    /// Begins the scope.
    /// </summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>
    /// Determines whether the enabled.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    /// <summary>
    /// Logs.
    /// </summary>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || ShouldSkip(category) || IsExpected(exception))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = exception?.Message ?? "(no message)";
        }

        var level = logLevel == LogLevel.Critical
            ? ApplicationErrorLevel.Critical
            : ApplicationErrorLevel.Error;

        var entry = new ErrorLogEntry(
            message,
            level,
            category,
            exception?.GetType().FullName,
            exception?.ToString(),
            ResolveErrorId());

        // Drop on full — never block the caller on the logging hot path.
        channel.TryWrite(entry);
    }

    // A caller (the API exception middleware) may pre-assign the captured row's id by logging inside
    // an ErrorLogScope.ErrorIdKey scope, so the API can return that id and deep-link to the error.
    private Guid? ResolveErrorId()
    {
        var scopeProvider = scopeAccessor?.Invoke();
        if (scopeProvider is null)
        {
            return null;
        }

        var box = new ErrorIdBox();
        scopeProvider.ForEachScope(static (scope, state) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (pair.Key == ErrorLogScope.ErrorIdKey && pair.Value is Guid guid)
                    {
                        state.Value = guid;
                    }
                }
            }
        }, box);

        return box.Value;
    }

    private sealed class ErrorIdBox { public Guid? Value; }

    private static bool ShouldSkip(string category)
        => category.StartsWith(SelfCategoryPrefix, StringComparison.Ordinal)
           || category.StartsWith(EfCategoryPrefix, StringComparison.Ordinal);

    // Cancellation is expected (client disconnect, shutdown) — not a real fault, never log it.
    // Covers TaskCanceledException, which derives from OperationCanceledException.
    private static bool IsExpected(Exception? exception)
        => exception is OperationCanceledException;
}
