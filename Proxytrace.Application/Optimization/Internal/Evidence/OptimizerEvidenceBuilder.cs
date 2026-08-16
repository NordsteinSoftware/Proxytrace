using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Application.Optimization.Internal.Evidence;

internal class OptimizerEvidenceBuilder : IOptimizerEvidenceBuilder
{
    private const int MaxFailing = 20;
    private const int PassingSampleSize = 3;

    /// <summary>
    /// Builds.
    /// </summary>
    public OptimizerEvidence Build(ITestRun run)
    {
        var failing = run.TestResults
            .Where(r => !r.IsPass())
            .OrderBy(r => (int?)r.OverallScore ?? 0)
            .ThenBy(r => r.Id)
            .Take(MaxFailing)
            .ToList();

        var passingSample = run.TestResults
            .Where(r => r.IsPass())
            .OrderBy(r => r.Id)
            .Take(PassingSampleSize)
            .ToList();

        return new OptimizerEvidence(run.Group.Suite.Agent, failing, passingSample);
    }
}
