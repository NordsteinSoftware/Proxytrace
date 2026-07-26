using JetBrains.Annotations;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Application.Optimization.Internal.Validation;

/// <summary>
/// Validates a tool-update theory by A/B-testing an ephemeral agent that carries the
/// proposed tools against the baseline run.
/// </summary>
[UsedImplicitly]
internal sealed class ToolUpdateTheoryValidator : AbTestTheoryValidator<IToolUpdateTheory>
{
    private readonly IToolUpdateProposal.CreateNew proposalFactory;
    private readonly IAgent.CreateNew agentFactory;

    public ToolUpdateTheoryValidator(
        IToolUpdateProposal.CreateNew proposalFactory,
        IAgent.CreateNew agentFactory,
        Lazy<ITestRunnerService> testRunnerService,
        ITestRunRepository testRuns,
        OptimizationOptions options)
        : base(testRunnerService, testRuns, options)
    {
        this.proposalFactory = proposalFactory;
        this.agentFactory = agentFactory;
    }

    protected override IAgent BuildCandidateAgent(IAgent agent, IToolUpdateTheory theory)
        => agentFactory(
            name: agent.Name,
            systemPrompt: agent.SystemPrompt,
            tools: theory.ProposedTools,
            endpoint: agent.Endpoint,
            project: agent.Project,
            modelParameters: agent.ModelParameters,
            isSystemAgent: agent.IsSystemAgent);

    protected override IOptimizationProposal BuildProposal(
        IToolUpdateTheory theory,
        double currentPassRate,
        double proposedPassRate,
        ITestRun candidateRun)
        => proposalFactory(
            agent: theory.Agent,
            priority: theory.Priority,
            rationale: theory.Rationale,
            proposedTools: theory.ProposedTools,
            currentPassRate: currentPassRate,
            proposedPassRate: proposedPassRate,
            evidenceTestRunIds: theory.EvidenceTestRunIds,
            abTestRun: candidateRun);
}
