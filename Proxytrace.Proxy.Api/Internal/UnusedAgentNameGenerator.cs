using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;

namespace Proxytrace.Proxy.Api.Internal;

/// <summary>
/// The storage repository graph (resolved when EF builds its model) depends on
/// <see cref="IAgentNameGenerator"/>, whose real implementation lives in the application layer the
/// proxy deliberately does not load. The proxy never creates agents — it only reads API keys — so
/// this stub satisfies the dependency and throws if it is ever actually invoked.
/// </summary>
internal sealed class UnusedAgentNameGenerator : IAgentNameGenerator
{
    /// <summary>
    /// Generates the name asynchronously.
    /// </summary>
    public Task<string> GenerateNameAsync(
        IPromptTemplate systemPrompt,
        IProject project,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Agent name generation is not available in the ingestion proxy.");
}
