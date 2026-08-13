using Nordstein.Core.Domain.Exceptions;

namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class EntityNotFoundExceptionMapper : IExceptionMapper
{
    public bool CanMap(Exception exception)
        => exception is EntityNotFoundException or EntitiesNotFoundException;

    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status404NotFound,
        TypeName = exception.GetType().Name,
    };
}
