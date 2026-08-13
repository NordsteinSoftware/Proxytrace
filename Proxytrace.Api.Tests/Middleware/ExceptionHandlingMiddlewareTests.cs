using System.Data.Common;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Middleware;
using Proxytrace.Api.Middleware.Exceptions;
using Nordstein.Core.Common.Net;
using Proxytrace.Domain.Exceptions;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;

namespace Proxytrace.Api.Tests.Middleware;

[TestClass]
public sealed class ExceptionHandlingMiddlewareTests
{
    private static ExceptionHandlingMiddleware Create(
        RequestDelegate next,
        bool isDevelopment = false,
        ILogger<ExceptionHandlingMiddleware>? logger = null)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");

        IExceptionMapper[] mappers =
        [
            new EntityNotFoundExceptionMapper(),
            new EntityConflictExceptionMapper(),
            new NotImplementedExceptionMapper(),
            new FeatureNotLicensedExceptionMapper(),
            new LicenseLimitExceededExceptionMapper(),
            new DbUpdateExceptionMapper(),
            new MalformedEndpointUrlExceptionMapper(),
        ];

        return new ExceptionHandlingMiddleware(
            next, logger ?? NullLogger<ExceptionHandlingMiddleware>.Instance, mappers, env);
    }

    private static async Task<(int Status, string Body)> InvokeAsync(ExceptionHandlingMiddleware middleware)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ctx.Response.Body);
        return (ctx.Response.StatusCode, await reader.ReadToEndAsync());
    }

    /// <summary>
    /// Runs the middleware against a context whose response has already started, and hands back the
    /// lifetime feature so the test can observe whether the connection was reset, plus the recorded
    /// log levels so it can observe whether the fault was captured into the Error Log.
    /// </summary>
    private static async Task<(IHttpRequestLifetimeFeature Lifetime, RecordingLogger Log)>
        InvokeAfterResponseStartedAsync(Exception thrown, bool clientDisconnected = false)
    {
        var response = Substitute.For<IHttpResponseFeature>();
        response.HasStarted.Returns(true);
        response.Headers.Returns(new HeaderDictionary());

        var lifetime = Substitute.For<IHttpRequestLifetimeFeature>();
        lifetime.RequestAborted.Returns(
            clientDisconnected ? new CancellationToken(canceled: true) : CancellationToken.None);

        var ctx = new DefaultHttpContext();
        ctx.Features.Set(response);
        ctx.Features.Set(lifetime);
        ctx.Request.Path = "/api/agent-calls/stream";

        var log = new RecordingLogger();
        await Create(_ => throw thrown, logger: log).InvokeAsync(ctx);
        return (lifetime, log);
    }

    private static JsonElement Error(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("error").Clone();
    }

    [TestMethod]
    public async Task InvokeAsync_NoException_DoesNotWriteErrorBody()
    {
        var middleware = Create(_ => Task.CompletedTask);

        var (status, body) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status200OK);
        body.Should().BeEmpty();
    }

    [TestMethod]
    public async Task InvokeAsync_EntityNotFound_Returns404()
    {
        var middleware = Create(_ => throw new EntityNotFoundException(Guid.NewGuid(), typeof(object)));

        var (status, body) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status404NotFound);
        Error(body).GetProperty("type").GetString().Should().Be(nameof(EntityNotFoundException));
    }

    [TestMethod]
    public async Task InvokeAsync_NotImplemented_Returns501()
    {
        var middleware = Create(_ => throw new NotImplementedException());

        var (status, _) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status501NotImplemented);
    }

    [TestMethod]
    public async Task InvokeAsync_FeatureNotLicensed_Returns402_WithFeatureFields()
    {
        var middleware = Create(_ =>
            throw new FeatureNotLicensedException(LicenseFeature.CustomEvaluators, LicenseTier.Free));

        var (status, body) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status402PaymentRequired);
        var error = Error(body);
        error.GetProperty("type").GetString().Should().Be("FeatureNotLicensed");
        error.GetProperty("feature").GetString().Should().Be(nameof(LicenseFeature.CustomEvaluators));
        error.GetProperty("tier").GetString().Should().Be(nameof(LicenseTier.Free));
    }

    [TestMethod]
    public async Task InvokeAsync_MalformedEndpointUrl_Returns400_WithMessage()
    {
        var middleware = Create(_ => throw new MalformedEndpointUrlException("not a url"));

        var (status, body) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status400BadRequest);
        var error = Error(body);
        error.GetProperty("type").GetString().Should().Be(nameof(MalformedEndpointUrlException));
        error.GetProperty("message").GetString().Should().Contain("not a url");
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownException_Returns500()
    {
        var middleware = Create(_ => throw new InvalidOperationException("boom"));

        var (status, _) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownExceptionInProduction_HidesExceptionMessage()
    {
        var middleware = Create(_ => throw new InvalidOperationException("boom"));

        var (_, body) = await InvokeAsync(middleware);

        Error(body).GetProperty("message").GetString().Should().Be("An unexpected error occurred.");
    }

    [TestMethod]
    public async Task InvokeAsync_UnknownExceptionInDevelopment_KeepsExceptionMessage()
    {
        var middleware = Create(_ => throw new InvalidOperationException("boom"), isDevelopment: true);

        var (_, body) = await InvokeAsync(middleware);

        Error(body).GetProperty("message").GetString().Should().Be("boom");
    }

    [TestMethod]
    public async Task InvokeAsync_DbUpdateForeignKeyViolation_Returns409_WithFriendlyMessage()
    {
        var inner = new FakeDbException("violates foreign key constraint \"FK_Agents_Projects\"", "23503");
        var middleware = Create(_ => throw new DbUpdateException("update failed", inner));

        var (status, body) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status409Conflict);
        var error = Error(body);
        error.GetProperty("type").GetString().Should().Be(nameof(DbUpdateException));
        var message = error.GetProperty("message").GetString();
        message.Should().Be("This record cannot be deleted or changed because other records still reference it.");
    }

    [TestMethod]
    public async Task InvokeAsync_DbUpdateWithoutForeignKeyViolation_Returns409_WithGenericConflictMessage()
    {
        var middleware = Create(_ => throw new DbUpdateException("update failed"));

        var (status, body) = await InvokeAsync(middleware);

        status.Should().Be(StatusCodes.Status409Conflict);
        Error(body).GetProperty("message").GetString().Should().Be("The change conflicts with existing data.");
    }

    [TestMethod]
    public async Task InvokeAsync_Development_IncludesStacktrace()
    {
        var middleware = Create(_ => throw new InvalidOperationException("boom"), isDevelopment: true);

        var (_, body) = await InvokeAsync(middleware);

        Error(body).GetProperty("stacktrace").GetString().Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task InvokeAsync_Production_OmitsStacktrace()
    {
        var middleware = Create(_ => throw new InvalidOperationException("boom"));

        var (_, body) = await InvokeAsync(middleware);

        Error(body).GetProperty("stacktrace").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestMethod]
    public async Task InvokeAsync_CapturedException_IncludesParsableErrorId()
    {
        var middleware = Create(_ => throw new InvalidOperationException("boom"));

        var (_, body) = await InvokeAsync(middleware);

        // The errorId is the captured Error Log row's id — an admin deep-links to it from the toast.
        var raw = Error(body).GetProperty("errorId").GetString();
        Guid.TryParse(raw, out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task InvokeAsync_NotImplemented_OmitsErrorId()
    {
        // 501 stubs are logged at Information, never captured — so there is nothing to link to.
        var middleware = Create(_ => throw new NotImplementedException());

        var (_, body) = await InvokeAsync(middleware);

        Error(body).GetProperty("errorId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestMethod]
    public async Task InvokeAsync_OperationCanceled_Propagates()
    {
        var middleware = Create(_ => throw new OperationCanceledException());

        await FluentActions
            .Invoking(() => InvokeAsync(middleware))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task InvokeAsync_ExceptionAfterResponseStarted_AbortsTheConnection()
    {
        // Returning normally would let the framework close the response cleanly, so the client would
        // read a truncated body as a complete one. The reset is the only remaining failure signal.
        var (lifetime, _) = await InvokeAfterResponseStartedAsync(new InvalidOperationException("boom"));

        lifetime.Received(1).Abort();
    }

    [TestMethod]
    public async Task InvokeAsync_ExceptionAfterResponseStarted_CapturesAnError()
    {
        // A genuine mid-stream fault on a live connection keeps its Error-level capture, so the
        // Error Log row (and the errorId that deep-links to it) still exists for an operator.
        var (_, log) = await InvokeAfterResponseStartedAsync(new InvalidOperationException("boom"));

        log.Levels.Should().Contain(LogLevel.Error);
    }

    [TestMethod]
    public async Task InvokeAsync_ExceptionAfterResponseStarted_DoesNotRethrow()
    {
        await FluentActions
            .Invoking(() => InvokeAfterResponseStartedAsync(new InvalidOperationException("boom")))
            .Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task InvokeAsync_ExceptionAfterClientDisconnected_DoesNotAbort()
    {
        // The connection is already gone — resetting it again would only add noise.
        var (lifetime, _) = await InvokeAfterResponseStartedAsync(
            new IOException("connection reset"), clientDisconnected: true);

        lifetime.DidNotReceive().Abort();
    }

    [TestMethod]
    public async Task InvokeAsync_ExceptionAfterClientDisconnected_DoesNotCaptureAnError()
    {
        // Every Error/Critical entry becomes an ApplicationError row, so an Error-level log here
        // would put one Error Log entry in front of operators per closed browser tab — for a
        // routine disconnect nobody can act on.
        var (_, log) = await InvokeAfterResponseStartedAsync(
            new IOException("connection reset"), clientDisconnected: true);

        log.Levels.Should().NotContain(LogLevel.Error);
    }

    [TestMethod]
    public async Task InvokeAsync_ExceptionAfterClientDisconnected_LogsOnceAtDebug()
    {
        var (_, log) = await InvokeAfterResponseStartedAsync(
            new IOException("connection reset"), clientDisconnected: true);

        log.Levels.Should().ContainSingle().Which.Should().Be(LogLevel.Debug);
    }

    /// <summary>
    /// Records the level of every entry the middleware writes. Only Error/Critical entries are
    /// picked up by the error-log capture pipeline, so the recorded levels tell a test whether a
    /// fault was captured as an <c>ApplicationError</c> row.
    /// </summary>
    private sealed class RecordingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeDbException : DbException
    {
        private readonly string sqlState;

        public FakeDbException(string message, string sqlState)
            : base(message)
        {
            this.sqlState = sqlState;
        }

        public override string SqlState => sqlState;
    }
}
