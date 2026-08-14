namespace Proxytrace.Domain.Proposal;

public interface IProposal : IDomainEntity
{
    
    
    Priority Priority { get; }
    string Description { get; }
}
