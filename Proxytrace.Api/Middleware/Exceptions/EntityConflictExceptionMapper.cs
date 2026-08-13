using Nordstein.Core.Domain.Exceptions;

namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class EntityConflictExceptionMapper : IExceptionMapper
{
    public bool CanMap(Exception exception)
        => exception is EntityAlreadyExistsException or OptimisticConcurrencyException;

    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status409Conflict,
        TypeName = exception.GetType().Name,
    };
}
