namespace Proxytrace.Api.Dto;

/// <summary>
/// Upper bounds for caller-supplied collections on request DTOs.
///
/// Without them the only ceiling is Kestrel's 30 MB body limit, and several handlers do one
/// database round-trip per element — some inside an open transaction. A single ~20 MB array of
/// GUIDs would issue hundreds of thousands of sequential queries while holding that transaction,
/// exhausting the connection pool and blocking every other write. The domain already enforces the
/// analogous caps it owns (<c>ICustomAnomalyDetector.MaxTriggers</c>,
/// <c>ITestRunGroup.MaxModelEndpoints</c>/<c>MaxSampleCount</c>); these cover the collections that
/// only ever exist at the API boundary.
///
/// <c>[ApiController]</c> turns a violation into an automatic 400 before the action body runs.
/// </summary>
internal static class RequestLimits
{
    /// <summary>Agents a single anomaly detector may be scoped to.</summary>
    public const int MaxScopedAgents = 500;

    /// <summary>Evaluators that may be attached to one test suite.</summary>
    public const int MaxEvaluators = 50;

    /// <summary>Test cases accepted in one suite create/update, and traces promoted in one call.</summary>
    public const int MaxTestCases = 500;
}
