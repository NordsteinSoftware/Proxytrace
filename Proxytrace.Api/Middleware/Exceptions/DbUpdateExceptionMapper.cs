using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Proxytrace.Api.Middleware.Exceptions;

/// <summary>
/// Safety net for <see cref="DbUpdateException"/>s that reach the middleware (controllers with
/// entity-specific messages catch them earlier, e.g. via <c>DeleteOrConflictAsync</c>): maps
/// constraint violations to a 409 with a friendly message instead of leaking the raw provider
/// error (constraint/table names) as a 500. The full exception is still logged and captured
/// into the application error log before mapping.
/// </summary>
internal sealed class DbUpdateExceptionMapper : IExceptionMapper
{
    // PostgreSQL SQLSTATE class 23 = integrity constraint violation.
    private const string ForeignKeyViolation = "23503";

    /// <summary>
    /// Determines whether the map.
    /// </summary>
    public bool CanMap(Exception exception) => exception is DbUpdateException;

    /// <summary>
    /// Maps.
    /// </summary>
    public ExceptionMapping Map(Exception exception) => new()
    {
        StatusCode = StatusCodes.Status409Conflict,
        TypeName = nameof(DbUpdateException),
        Message = IsForeignKeyViolation(exception)
            ? "This record cannot be deleted or changed because other records still reference it."
            : "The change conflicts with existing data.",
    };

    private static bool IsForeignKeyViolation(Exception exception)
        => exception.InnerException is DbException { SqlState: ForeignKeyViolation };
}
