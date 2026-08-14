using JetBrains.Annotations;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Application.Optimization.Internal.Validation;

/// <summary>
/// Validates a system-prompt theory by A/B-testing an ephemeral agent that carries the
/// proposed prompt against the baseline run.
/// </summary>
[UsedImplicitly]
internal sealed class SystemPromptTheoryValidator : AbTestTheoryValidator<ISystemPromptTheory>
{
    private readonly ISystemPromptProposal.CreateNew proposalFactory;
    private readonly IPromptTemplate.Create promptTemplateFactory;
    private readonly IAgent.CreateNew agentFactory;

    public SystemPromptTheoryValidator(
        ISystemPromptProposal.CreateNew proposalFactory,
        IPromptTemplate.Create promptTemplateFactory,
        IAgent.CreateNew agentFactory,
        Lazy<ITestRunnerService> testRunnerService,
        ITestRunRepository testRuns,
        OptimizationOptions options)
        : base(testRunnerService, testRuns, options)
    {
        this.proposalFactory = proposalFactory;
        this.promptTemplateFactory = promptTemplateFactory;
        this.agentFactory = agentFactory;
    }

    protected override IAgent BuildCandidateAgent(IAgent agent, ISystemPromptTheory theory)
    {
        var promptTemplate = promptTemplateFactory(agent.Name, theory.ProposedSystemMessage);
        return agentFactory(
            name: agent.Name,
            systemPrompt: promptTemplate,
            tools: agent.Tools,
            endpoint: agent.Endpoint,
            project: agent.Project,
            modelParameters: agent.ModelParameters,
            isSystemAgent: agent.IsSystemAgent);
    }

    protected override IOptimizationProposal BuildProposal(
        ISystemPromptTheory theory,
        double currentPassRate,
        double proposedPassRate,
        ITestRun candidateRun)
        => proposalFactory(
            agent: theory.Agent,
            priority: theory.Priority,
            rationale: theory.Rationale,
            proposedSystemMessage: theory.ProposedSystemMessage,
            currentPassRate: currentPassRate,
            proposedPassRate: proposedPassRate,
            evidenceTestRunIds: theory.EvidenceTestRunIds,
            abTestRun: candidateRun);
}
