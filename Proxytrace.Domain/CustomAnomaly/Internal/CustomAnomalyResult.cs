using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.CustomAnomaly.Internal;

internal record CustomAnomalyResult : DomainEntity<ICustomAnomalyResult>, ICustomAnomalyResult
{
    /// <summary>
    /// Gets the detector id.
    /// </summary>
    public Guid DetectorId { get; }
    /// <summary>
    /// Gets the agent call id.
    /// </summary>
    public Guid AgentCallId { get; }
    /// <summary>
    /// Gets the project id.
    /// </summary>
    public Guid ProjectId { get; }
    /// <summary>
    /// Gets the matched trigger.
    /// </summary>
    public string MatchedTrigger { get; }
    /// <summary>
    /// Gets the reasoning.
    /// </summary>
    public string? Reasoning { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyResult"/> class.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyResult"/> class.
    /// </summary>
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

    /// <summary>
    /// Validates.
    /// </summary>
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
