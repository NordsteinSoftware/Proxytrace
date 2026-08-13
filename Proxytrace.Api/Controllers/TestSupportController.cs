using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Domain.TestSupport;
using Nordstein.Core.Common.Text;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// Test-only endpoints that let the e2e suite control server state. Not part of the product
/// surface — mirrors the existing per-controller <c>/seed</c> endpoints used by the same suite.
/// </summary>
[ApiController]
[Authorize]
[TestOnlyEndpoint]
[Route("api/test")]
public class TestSupportController : ControllerBase
{
    private readonly ITestDataReset reset;
    private readonly ILogger<TestSupportController> logger;

    public TestSupportController(ITestDataReset reset, ILogger<TestSupportController> logger)
    {
        this.reset = reset;
        this.logger = logger;
    }

    /// <summary>
    /// Clears all per-run domain content (agents, traces, evaluators, suites, runs, proposals,
    /// invites, MFA enrollments, notifications) while preserving the setup baseline, so a spec can
    /// start from a known state.
    /// </summary>
    /// <remarks>
    /// Anonymous on purpose: the reset must be reachable to restore a clean baseline even when the
    /// shared admin is in a state that blocks login (e.g. an MFA spec enabled a second factor — a
    /// password login then returns an MFA challenge with no session token). Requiring auth created a
    /// chicken-and-egg where the reset that clears the MFA enrollment could not authenticate to run.
    /// Safe because <see cref="TestOnlyEndpointAttribute"/> already 404s this endpoint on any real
    /// deployment (only Development or <c>TestSupport:Enabled</c> reaches it).
    /// </remarks>
    [HttpPost("reset")]
    [AllowAnonymous]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        await reset.ResetAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Emits a real Error/Critical log entry so the e2e suite can exercise the error-capture
    /// pipeline end-to-end (logger → channel → writer → ApplicationError row → Error Log UI).
    /// </summary>
    [HttpPost("log-error")]
    public IActionResult LogError([FromBody] LogErrorRequest request)
    {
        try
        {
            // Thrown (then caught) so the captured entry carries a real stacktrace, mirroring a
            // genuine failure rather than a bare logged message.
            throw new InvalidOperationException(request.Message);
        }
        catch (InvalidOperationException exception)
        {
            if (request.Critical)
            {
                logger.LogCritical(exception, "{Message}", request.Message.ToSingleLogLine());
            }
            else
            {
                logger.LogError(exception, "{Message}", request.Message.ToSingleLogLine());
            }
        }

        return Accepted();
    }
}

/// <summary>
/// Request body for the test-only log-error endpoint.
/// </summary>
public record LogErrorRequest(string Message, bool Critical = false);
