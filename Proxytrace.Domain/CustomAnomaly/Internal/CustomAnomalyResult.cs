using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.CustomAnomaly.Internal;

internal record CustomAnomalyResult : DomainEntity<ICustomAnomalyResult>, ICustomAnomalyResult
{
    public Guid DetectorId { get; }
    public Guid AgentCallId { get; }
    public Guid ProjectId { get; }
    public string MatchedTrigger { get; }
    public string? Reasoning { get; }

    public CustomAnomalyResult(
        Guid detectorId,
        Guid agentCallId,
        Guid projectId,
        string matchedTrigger,
        string? reasoning,
        IRepository<ICustomAnomalyResult> repository) : base(repository)
    {
        DetectorId = detectorId;
        AgentCallId = agentCallId;
        ProjectId = projectId;
        MatchedTrigger = matchedTrigger;
        Reasoning = reasoning;
    }

    public CustomAnomalyResult(
        Guid detectorId,
        Guid agentCallId,
        Guid projectId,
        string matchedTrigger,
        string? reasoning,
        IDomainEntityData existing,
        IRepository<ICustomAnomalyResult> repository) : base(existing, repository)
    {
        DetectorId = detectorId;
        AgentCallId = agentCallId;
        ProjectId = projectId;
        MatchedTrigger = matchedTrigger;
        Reasoning = reasoning;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotDefault(DetectorId);
        yield return Validation.NotDefault(AgentCallId);
        yield return Validation.NotDefault(ProjectId);
        yield return Validation.NotNullOrWhiteSpace(MatchedTrigger);
    }
}
