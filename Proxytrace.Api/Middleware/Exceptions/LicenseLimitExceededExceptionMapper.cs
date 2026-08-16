using Proxytrace.Licensing.Exceptions;

namespace Proxytrace.Api.Middleware.Exceptions;

internal sealed class LicenseLimitExceededExceptionMapper : IExceptionMapper
{
    /// <summary>
    /// Determines whether the map.
    /// </summary>
    public bool CanMap(Exception exception) => exception is LicenseLimitExceededException;

    /// <summary>
    /// Maps.
    /// </summary>
    public ExceptionMapping Map(Exception exception)
    {
        var limit = (LicenseLimitExceededException)exception;
        return new ExceptionMapping
        {
            StatusCode = StatusCodes.Status402PaymentRequired,
            TypeName = "LicenseLimitExceeded",
            AdditionalFields = new Dictionary<string, object?>
            {
                ["limit"] = limit.Limit.ToString(),
                ["current"] = limit.Current,
                ["max"] = limit.Max,
            },
        };
    }
}
