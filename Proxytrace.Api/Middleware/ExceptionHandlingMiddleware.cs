using System.Text.Json;
using Proxytrace.Api.Middleware.Exceptions;
using Proxytrace.Application.ErrorLog;
using Proxytrace.Common.Text;

namespace Proxytrace.Api.Middleware;

internal sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<ExceptionHandlingMiddleware> logger;
    private readonly IEnumerable<IExceptionMapper> mappers;
    private readonly bool isDevelopment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IEnumerable<IExceptionMapper> mappers,
        IWebHostEnvironment env)
    {
        this.next = next;
        this.logger = logger;
        this.mappers = mappers;
        this.isDevelopment = env.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Classify a client that hung up mid-stream *first*, before anything is logged at Error
            // level. Such a disconnect surfaces as a failed write (typically an IOException) rather
            // than an OperationCanceledException, so it reaches this catch like a genuine fault —
            // and capturing it would persist an ApplicationError row, carrying an errorId nobody can
            // act on, for every browser tab closed on a page with an open SSE stream. There is no
            // connection left to reset either, and no error body can be written into a response that
            // has already started, so a single Debug line is the whole handling.
            if (context.Response.HasStarted && context.RequestAborted.IsCancellationRequested)
            {
                logger.LogDebug(
                    ex,
                    "Request aborted by the client after the response had already started for {Path}",
                    context.Request.Path.Value.ToSingleLogLine());
                return;
            }

            // Pre-assign the captured error's id so it can be returned to the client (for an
            // admin deep-link into the Error Log). Only meaningful when we actually capture, i.e.
            // the Error/Critical branch below — a not-implemented stub is logged at Information.
            Guid? errorId = null;

            // NotImplementedException marks an intentional stub — surface it as 501
            // without the alarming error-level log.
            if (ex is NotImplementedException)
            {
                logger.LogInformation(
                    "Not-implemented endpoint called: {Path}",
                    context.Request.Path.Value.ToSingleLogLine());
            }
            else
            {
                errorId = Guid.NewGuid();
                // The scope carries the id into the error-log capture pipeline so the persisted
                // row's primary key matches the id we return below.
                using (logger.BeginScope(new Dictionary<string, object> { [ErrorLogScope.ErrorIdKey] = errorId.Value }))
                {
                    logger.LogError(ex, "Unhandled exception");
                }
            }

            // Once the response has started (every SSE stream writes its headers and first frames
            // long before its work can fail) the status and headers are read-only: assigning them
            // throws InvalidOperationException, that secondary exception escapes this catch, and the
            // original fault is replaced by a bare connection abort. An error body is equally
            // useless — it would be appended to a payload the client is already consuming.
            // Reaching here with a started response means the client is still connected (the
            // disconnect case returned above), so this is a genuine fault on a live stream.
            if (context.Response.HasStarted)
            {
                logger.LogWarning(
                    ex,
                    "Unhandled exception after the response had already started for {Path}; resetting the connection",
                    context.Request.Path.Value.ToSingleLogLine());

                // Returning without aborting would signal *success* to the framework: it finishes the
                // response cleanly (chunked terminator / HTTP/2 END_STREAM) and the client reads a
                // well-formed but truncated payload as a complete one. Aborting resets the connection
                // so the truncation surfaces as a transport error the caller cannot mistake for a
                // short-but-valid result.
                context.Abort();
                return;
            }

            context.Response.ContentType = "application/json";

            var mapping = Resolve(ex);
            context.Response.StatusCode = mapping.StatusCode;

            var error = new Dictionary<string, object?>
            {
                ["message"] = mapping.Message ?? ex.Message,
                ["type"] = mapping.TypeName,
                ["stacktrace"] = isDevelopment ? ex.ToString() : null,
                ["errorId"] = errorId,
            };

            if (mapping.AdditionalFields is not null)
            {
                foreach (var (key, value) in mapping.AdditionalFields)
                    error[key] = value;
            }

            var response = JsonSerializer.Serialize(
                new Dictionary<string, object?> { ["error"] = error },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            await context.Response.WriteAsync(response);
        }
    }

    private ExceptionMapping Resolve(Exception exception)
    {
        foreach (var mapper in mappers)
        {
            if (mapper.CanMap(exception))
                return mapper.Map(exception);
        }

        // Unmapped exceptions are internal faults — outside development their message must not
        // reach the client (it may carry SQL, schema names, paths). Full detail is preserved in
        // the log capture (application error log) above.
        return new ExceptionMapping
        {
            StatusCode = StatusCodes.Status500InternalServerError,
            TypeName = exception.GetType().Name,
            Message = isDevelopment ? null : "An unexpected error occurred.",
        };
    }
}
