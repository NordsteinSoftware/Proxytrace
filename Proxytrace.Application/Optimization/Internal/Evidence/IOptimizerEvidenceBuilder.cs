using Proxytrace.Domain.TestRun;

namespace Proxytrace.Application.Optimization.Internal.Evidence;

internal interface IOptimizerEvidenceBuilder
{
    OptimizerEvidence Build(ITestRun run);
}
