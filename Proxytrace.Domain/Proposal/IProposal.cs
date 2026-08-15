namespace Proxytrace.Domain.Proposal;

/// <summary>
/// Represents a proposal.
/// </summary>
public interface IProposal : IDomainEntity
{
    
    
    Priority Priority { get; }
    string Description { get; }
}
