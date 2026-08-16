using Proxytrace.Domain.Agent;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;

namespace Proxytrace.Application.Tracey.Internal;

internal sealed class TraceyAgentProvisioner : ITraceyAgentProvisioner
{
    /// <summary>
    /// The canonical agent name used to seed, find, and attribute Tracey within a project. Tracey's
    /// live system prompt and tools are owned by the frontend runtime
    /// (<c>frontend/src/features/tracey/{tracey-prompt,tracey-tools}.ts</c>) and captured into her
    /// agent version on her first call via name-based attribution, so nothing else lives here.
    /// </summary>
    public const string AgentName = "Tracey";

    // The seeded agent needs a v1 prompt before her first call lands. This placeholder is overwritten
    // by the live prompt captured from the wire.
    private const string SeedPrompt =
        "You are Tracey, the in-app assistant for Proxytrace. This placeholder identity prompt is " +
        "replaced by the live prompt captured from her first call.";

    private readonly IAgentRepository agents;
    private readonly IAgent.CreateNew createAgent;
    private readonly IPromptTemplate.Create createPrompt;
    private readonly IModelParameters.Create createParameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceyAgentProvisioner"/> class.
    /// </summary>
    public TraceyAgentProvisioner(
        IAgentRepository agents,
        IAgent.CreateNew createAgent,
        IPromptTemplate.Create createPrompt,
        IModelParameters.Create createParameters)
    {
        this.agents = agents;
        this.createAgent = createAgent;
        this.createPrompt = createPrompt;
        this.createParameters = createParameters;
    }

    /// <summary>
    /// Ensures the tracey agent asynchronously.
    /// </summary>
    public async Task<IAgent> EnsureTraceyAgentAsync(IProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var existing = await FindTraceyAgentAsync(project, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // Seed identity only: empty tools + a placeholder prompt. Tracey's live prompt + tools are
        // captured from her first call and versioned under this agent via name-based attribution.
        var agent = createAgent(
            AgentName,
            createPrompt("tracey-system", SeedPrompt),
            tools: [],
            endpoint: project.SystemEndpoint,
            project: project,
            modelParameters: createParameters(temperature: 0.2),
            isSystemAgent: true);

        return await agent.AddAsync(cancellationToken);
    }

    private async Task<IAgent?> FindTraceyAgentAsync(IProject project, CancellationToken cancellationToken)
    {
        var agent = await agents.FindByNameAsync(project, AgentName, cancellationToken);
        return agent is { IsSystemAgent: true } ? agent : null;
    }
}
