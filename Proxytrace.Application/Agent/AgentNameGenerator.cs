using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Agent;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;

namespace Proxytrace.Application.Agent;

[UsedImplicitly]
internal sealed class AgentNameGenerator : IAgentNameGenerator
{
    private const string PromptName = "agent_name_generator";

    private readonly IPromptTemplateRepository prompts;
    private readonly IAgentRepository agentRepository;
    private readonly ILogger<AgentNameGenerator> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentNameGenerator"/> class.
    /// </summary>
    public AgentNameGenerator(
        IPromptTemplateRepository prompts,
        IAgentRepository agentRepository,
        ILogger<AgentNameGenerator> logger)
    {
        this.prompts = prompts;
        this.agentRepository = agentRepository;
        this.logger = logger;
    }

    /// <summary>
    /// Generates the name asynchronously.
    /// </summary>
    public async Task<string> GenerateNameAsync(
        IPromptTemplate promptTemplate,
        IProject project,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agent = await GetGeneratorAgent(project, cancellationToken);
            using var client = agent.CreateClient();
            var result = await client
                .CompleteAsync(
                    Message.CreateUserMessage(promptTemplate.Template),
                    cancellationToken: cancellationToken);
            return result.Response.GetTextResponse();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent name generation failed for project {ProjectId}", project.Id);
            throw;
        }
    }

    private async Task<IAgent> GetGeneratorAgent(IProject project, CancellationToken cancellationToken)
        => await agentRepository.GetOrCreateAsync(
            name: PromptName,
            systemPrompt: await prompts.GetAsync(PromptName, cancellationToken),
            project: project,
            endpoint: project.SystemEndpoint,
            tools: [],
            isSystemAgent: true,
            cancellationToken: cancellationToken);
}
