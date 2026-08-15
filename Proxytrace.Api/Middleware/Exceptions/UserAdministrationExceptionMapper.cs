using Proxytrace.Application.Auth;

namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class UserAdministrationExceptionMapper : IExceptionMapper
{
    /// <summary>
    /// Determines whether the map.
    /// </summary>
    public bool CanMap(Exception exception)
        => exception is UserAdministrationException;

    /// <summary>
    /// Maps.
    /// </summary>
    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status409Conflict,
        TypeName = exception.GetType().Name,
    };
}
