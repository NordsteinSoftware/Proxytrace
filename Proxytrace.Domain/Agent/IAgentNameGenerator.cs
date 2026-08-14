using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;

namespace Proxytrace.Domain.Agent;

/// <summary>
/// Generates a short, human-readable name for an agent from its system message, using the agent's own model endpoint.
/// </summary>
public interface IAgentNameGenerator
{
    Task<string> GenerateNameAsync(
        IPromptTemplate systemPrompt,
        IProject project,
        CancellationToken cancellationToken = default);
}
