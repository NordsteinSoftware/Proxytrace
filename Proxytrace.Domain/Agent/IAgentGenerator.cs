namespace Proxytrace.Domain.Agent;

public interface IAgentGenerator : IDomainEntityGenerator<IAgent>
{
    Task<IAgent> CreateAsync(
        string name,
        string? systemPrompt = null, 
        bool isSystemAgent = false,
        CancellationToken cancellationToken = default);
}
