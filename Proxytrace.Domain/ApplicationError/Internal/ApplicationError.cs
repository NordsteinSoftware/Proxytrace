using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.ApplicationError.Internal;

internal record ApplicationError : DomainEntity<IApplicationError>, IApplicationError
{
    /// <summary>
    /// Gets the message.
    /// </summary>
    public string Message { get; }
    /// <summary>
    /// Gets the level.
    /// </summary>
    public ApplicationErrorLevel Level { get; }
    /// <summary>
    /// Gets the category.
    /// </summary>
    public string Category { get; }
    /// <summary>
    /// Gets the exception type.
    /// </summary>
    public string? ExceptionType { get; }
    /// <summary>
    /// Gets the stack trace.
    /// </summary>
    public string? StackTrace { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationError"/> class.
    /// </summary>
    public ApplicationError(
        string message,
        ApplicationErrorLevel level,
        string category,
        string? exceptionType,
        string? stackTrace,
        IRepository<IApplicationError> repository) : base(repository)
    {
        Message = message;
        Level = level;
        Category = category;
        ExceptionType = exceptionType;
        StackTrace = stackTrace;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationError"/> class.
    /// </summary>
    public ApplicationError(
        string message,
        ApplicationErrorLevel level,
        string category,
        string? exceptionType,
        string? stackTrace,
        IDomainEntityData existing,
        IRepository<IApplicationError> repository) : base(existing, repository)
    {
        Message = message;
        Level = level;
        Category = category;
        ExceptionType = exceptionType;
        StackTrace = stackTrace;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        yield return Validation.NotNullOrWhiteSpace(Message);
        yield return Validation.NotNullOrWhiteSpace(Category);
        yield return Validation.Defined(Level);
    }
}
