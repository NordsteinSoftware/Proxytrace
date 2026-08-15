namespace Proxytrace.Domain.AgentCall;

/// <summary>
/// Generates agent call instances.
/// </summary>
public interface IAgentCallGenerator : IDomainEntityGenerator<IAgentCall>
{
    Task<IAgentCall> CreateAsync(DateTimeOffset createdAt,  CancellationToken cancellationToken = default);
}
