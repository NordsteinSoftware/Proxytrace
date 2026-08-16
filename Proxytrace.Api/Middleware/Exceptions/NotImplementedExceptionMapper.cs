namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class NotImplementedExceptionMapper : IExceptionMapper
{
    /// <summary>
    /// Determines whether the map.
    /// </summary>
    public bool CanMap(Exception exception) => exception is NotImplementedException;

    /// <summary>
    /// Maps.
    /// </summary>
    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status501NotImplemented,
        TypeName = exception.GetType().Name,
    };
}
