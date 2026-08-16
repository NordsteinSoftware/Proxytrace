namespace Proxytrace.Domain.Proposal;

/// <summary>
/// Common base for actionable optimization proposals surfaced to users, covering system-prompt,
/// tool-list, and model-switch changes. Each proposal carries a priority and a human-readable
/// rationale derived from the evidence that motivated it.
/// </summary>
public interface IProposal : IDomainEntity
{
    /// <summary>How urgently the proposed change should be adopted.</summary>
    Priority Priority { get; }

    /// <summary>Human-readable description of what this proposal recommends and why.</summary>
    string Description { get; }
}
