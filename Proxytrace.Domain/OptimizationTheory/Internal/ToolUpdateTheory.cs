using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Tools;

namespace Proxytrace.Domain.OptimizationTheory.Internal;

[UsedImplicitly]
internal record ToolUpdateTheory : OptimizationTheory, IToolUpdateTheory
{
    public override ProposalKind Kind => ProposalKind.Tool;
    public IReadOnlyList<ToolSpecification> ProposedTools { get; private init; }

    public ToolUpdateTheory(
        IAgent agent,
        ITestSuite suite,
        TheorySource source,
        Priority priority,
        string rationale,
        IReadOnlyList<ToolSpecification> proposedTools,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ISerializer serializer,
        IRepository<IOptimizationTheory> repository)
        : base(agent, suite, source, priority, rationale, evidenceTestRunIds,
            OptimizationContentHash.ForTools(serializer, agent.Id, proposedTools), repository)
    {
        ProposedTools = proposedTools.ToArray();
    }

    public ToolUpdateTheory(
        IAgent agent,
        ITestSuite suite,
        TheoryStatus status,
        TheorySource source,
        Priority priority,
        string rationale,
        IReadOnlyList<ToolSpecification> proposedTools,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        Guid? resultingProposalId,
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        string contentHash,
        IDomainEntityData existing,
        IRepository<IOptimizationTheory> repository)
        : base(agent, suite, status, source, priority, rationale, evidenceTestRunIds,
            resultingProposalId, baselinePassRate, projectedPassRate, pValue, abTestRunId, contentHash, existing, repository)
    {
        ProposedTools = proposedTools.ToArray();
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in ProposedTools.SelectMany(tool => tool.Validate(validationContext)))
            yield return result;
    }
}
