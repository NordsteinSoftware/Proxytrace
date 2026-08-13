using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.OptimizationTheory.Internal;

[UsedImplicitly]
internal record ModelSwitchTheory : OptimizationTheory, IModelSwitchTheory
{
    public override ProposalKind Kind => ProposalKind.ModelSwitch;
    public IModelEndpoint ProposedEndpoint { get; private init; }

    public ModelSwitchTheory(
        IAgent agent,
        ITestSuite suite,
        TheorySource source,
        Priority priority,
        string rationale,
        IModelEndpoint proposedEndpoint,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ISerializer serializer,
        IRepository<IOptimizationTheory> repository)
        : base(agent, suite, source, priority, rationale, evidenceTestRunIds,
            OptimizationContentHash.ForModelSwitch(serializer, agent.Id, proposedEndpoint.Id), repository)
    {
        ProposedEndpoint = proposedEndpoint;
    }

    public ModelSwitchTheory(
        IAgent agent,
        ITestSuite suite,
        TheoryStatus status,
        TheorySource source,
        Priority priority,
        string rationale,
        IModelEndpoint proposedEndpoint,
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
        ProposedEndpoint = proposedEndpoint;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in ProposedEndpoint.Validate(validationContext))
            yield return result;
    }
}
