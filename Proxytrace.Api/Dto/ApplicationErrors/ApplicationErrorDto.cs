using Proxytrace.Domain.ApplicationError;

namespace Proxytrace.Api.Dto.ApplicationErrors;

/// <summary>
/// Data transfer object representing a application error.
/// </summary>
public record ApplicationErrorDto(
    Guid Id,
    string Message,
    ApplicationErrorLevel Level,
    string Category,
    string? ExceptionType,
    string? StackTrace,
    DateTimeOffset CreatedAt);
