using Nordstein.Core.Common.Net;

namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class MalformedEndpointUrlExceptionMapper : IExceptionMapper
{
    public bool CanMap(Exception exception)
        => exception is MalformedEndpointUrlException;

    // The message only echoes the user's own input plus a format hint — no internals — so it is
    // safe to surface as the 400 body.
    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status400BadRequest,
        TypeName = exception.GetType().Name,
        Message = exception.Message,
    };
}
