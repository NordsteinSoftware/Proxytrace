using Nordstein.Core.Domain.Exceptions;

namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class EntityConflictExceptionMapper : IExceptionMapper
{
    /// <summary>
    /// Determines whether the map.
    /// </summary>
    public bool CanMap(Exception exception)
        => exception is EntityAlreadyExistsException or OptimisticConcurrencyException;

    /// <summary>
    /// Maps.
    /// </summary>
    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status409Conflict,
        TypeName = exception.GetType().Name,
    };
}
