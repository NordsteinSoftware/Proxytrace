using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;

namespace Proxytrace.Application.Evaluator.Internal;

internal sealed class DefaultEvaluatorProvisioner : IDefaultEvaluatorProvisioner
{
    private readonly IAgenticEvaluatorPresets presets;
    private readonly IAgentRepository agents;
    private readonly IEvaluatorRepository evaluators;
    private readonly IAgent.CreateNew createAgent;
    private readonly IAgenticEvaluator.CreateNew createAgentic;
    private readonly IExactMatchEvaluator.CreateNew createExactMatch;
    private readonly IPromptTemplate.Create createPrompt;
    private readonly IModelParameters.Create createParameters;

    public DefaultEvaluatorProvisioner(
        IAgenticEvaluatorPresets presets,
        IAgentRepository agents,
        IEvaluatorRepository evaluators,
        IAgent.CreateNew createAgent,
        IAgenticEvaluator.CreateNew createAgentic,
        IExactMatchEvaluator.CreateNew createExactMatch,
        IPromptTemplate.Create createPrompt,
        IModelParameters.Create createParameters)
    {
        this.presets = presets;
        this.agents = agents;
        this.evaluators = evaluators;
        this.createAgent = createAgent;
        this.createAgentic = createAgentic;
        this.createExactMatch = createExactMatch;
        this.createPrompt = createPrompt;
        this.createParameters = createParameters;
    }

    public async Task EnsureDefaultEvaluatorsAsync(IProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var existing = await evaluators.GetByProjectAsync(project.Id, cancellationToken);

        // Dedup by evaluator name (an agentic evaluator's Name equals its agent's name; the exact
        // match evaluator's Name is the fixed "Exact Match"). Keeps the method idempotent across
        // both the on-create call and the startup backfill.
        var existingNames = existing
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (!existing.Any(e => e.Kind == EvaluatorKind.ExactMatch))
        {
            var exactMatch = createExactMatch(project);
            await evaluators.AddAsync(exactMatch, cancellationToken);
        }

        foreach (var preset in presets.GetAll())
        {
            if (existingNames.Contains(preset.Name))
            {
                continue;
            }

            await CreateAgenticAsync(project, preset, cancellationToken);
        }
    }

    private async Task CreateAgenticAsync(
        IProject project,
        AgenticEvaluatorPreset preset,
        CancellationToken cancellationToken)
    {
        // Reuse an existing system agent of this name (find-then-create) so a half-provisioned
        // project doesn't accumulate orphan agents — mirrors the Tracey provisioner.
        var agent = await agents.FindByNameAsync(project, preset.Name, cancellationToken);
        if (agent is not { IsSystemAgent: true })
        {
            agent = await createAgent(
                name: preset.Name,
                systemPrompt: createPrompt(preset.Name, preset.SystemPrompt),
                tools: [],
                endpoint: project.SystemEndpoint,
                project: project,
                modelParameters: createParameters(temperature: 0.0),
                isSystemAgent: true).AddAsync(cancellationToken);
        }

        var evaluator = createAgentic(agent);
        await evaluators.AddAsync(evaluator, cancellationToken);
    }
}
