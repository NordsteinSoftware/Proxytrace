using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.ApplicationError.Internal;

internal record ApplicationError : DomainEntity<IApplicationError>, IApplicationError
{
    public string Message { get; }
    public ApplicationErrorLevel Level { get; }
    public string Category { get; }
    public string? ExceptionType { get; }
    public string? StackTrace { get; }

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
