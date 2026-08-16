using Nordstein.Core.Domain.Exceptions;

namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class EntityNotFoundExceptionMapper : IExceptionMapper
{
    /// <summary>
    /// Determines whether the map.
    /// </summary>
    public bool CanMap(Exception exception)
        => exception is EntityNotFoundException or EntitiesNotFoundException;

    /// <summary>
    /// Maps.
    /// </summary>
    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status404NotFound,
        TypeName = exception.GetType().Name,
    };
}
