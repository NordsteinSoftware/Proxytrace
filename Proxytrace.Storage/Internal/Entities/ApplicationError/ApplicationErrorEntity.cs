using Proxytrace.Domain.ApplicationError;

namespace Proxytrace.Storage.Internal.Entities.ApplicationError;

[StoredDomainEntity(typeof(IApplicationError))]
internal record ApplicationErrorEntity : Entity
{
    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets or sets the level.
    /// </summary>
    public required ApplicationErrorLevel Level { get; init; }

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets or sets the exception type.
    /// </summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Gets or sets the stack trace.
    /// </summary>
    public string? StackTrace { get; init; }
}
