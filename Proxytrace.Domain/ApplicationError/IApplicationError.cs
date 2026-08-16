namespace Proxytrace.Domain.ApplicationError;

/// <summary>
/// A captured application error — one <c>Error</c>/<c>Critical</c> log entry persisted for
/// inspection in the Error Log UI. Immutable: errors are recorded once and never mutated.
/// </summary>
public interface IApplicationError : IDomainEntity<IApplicationError>
{
    /// <summary>The log message or exception message.</summary>
    string Message { get; }

    /// <summary>Severity of the entry (<c>Error</c> or <c>Critical</c>).</summary>
    ApplicationErrorLevel Level { get; }

    /// <summary>The logger category (source), e.g. the fully-qualified type name that logged it.</summary>
    string Category { get; }

    /// <summary>The exception type name, or <see langword="null"/> for a bare log with no exception.</summary>
    string? ExceptionType { get; }

    /// <summary>The full exception stacktrace, or <see langword="null"/> when no exception was logged.</summary>
    string? StackTrace { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IApplicationError CreateNew(
        string message,
        ApplicationErrorLevel level,
        string category,
        string? exceptionType,
        string? stackTrace);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IApplicationError CreateExisting(
        string message,
        ApplicationErrorLevel level,
        string category,
        string? exceptionType,
        string? stackTrace,
        IDomainEntityData existing);
}
