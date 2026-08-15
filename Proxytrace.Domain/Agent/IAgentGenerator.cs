namespace Proxytrace.Domain.Agent;

/// <summary>
/// Generates agent instances.
/// </summary>
public interface IAgentGenerator : IDomainEntityGenerator<IAgent>
{
    Task<IAgent> CreateAsync(
        string name,
        string? systemPrompt = null, 
        bool isSystemAgent = false,
        CancellationToken cancellationToken = default);
}
