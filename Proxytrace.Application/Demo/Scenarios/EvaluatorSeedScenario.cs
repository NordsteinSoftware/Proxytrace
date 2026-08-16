using Proxytrace.Application.Evaluator;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;

namespace Proxytrace.Application.Demo.Scenarios;

internal sealed class EvaluatorSeedScenario : IDemoScenario
{
    private readonly DemoSeedContext ctx;
    private readonly IAgenticEvaluatorPresets presets;
    private readonly IPromptTemplate.Create createPromptTemplate;
    private readonly IModelParameters.Create createParams;
    private readonly IAgent.CreateNew createAgent;
    private readonly IAgenticEvaluator.CreateNew createAgentic;
    private readonly IRepository<IEvaluator> evaluatorRepo;

    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluatorSeedScenario"/> class.
    /// </summary>
    public EvaluatorSeedScenario(
        DemoSeedContext ctx,
        IAgenticEvaluatorPresets presets,
        IPromptTemplate.Create createPromptTemplate,
        IModelParameters.Create createParams,
        IAgent.CreateNew createAgent,
        IAgenticEvaluator.CreateNew createAgentic,
        IRepository<IEvaluator> evaluatorRepo)
    {
        this.ctx = ctx;
        this.presets = presets;
        this.createPromptTemplate = createPromptTemplate;
        this.createParams = createParams;
        this.createAgent = createAgent;
        this.createAgentic = createAgentic;
        this.evaluatorRepo = evaluatorRepo;
    }

    /// <summary>
    /// Gets the order.
    /// </summary>
    public int Order => 10;

    /// <summary>
    /// Seeds asynchronously.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var allPresets = presets.GetAll();
        var project = ctx.RequireProject();

        ctx.Helpfulness = await SeedFromPreset("helpfulness");
        ctx.Politeness = await SeedFromPreset("politeness");

        return;

        async Task<IAgenticEvaluator> SeedFromPreset(string presetKey)
        {
            var preset = allPresets.FirstOrDefault(p => p.Key == presetKey)
                ?? throw new InvalidOperationException(
                    $"Agentic evaluator preset '{presetKey}' is not registered.");

            var template = createPromptTemplate(preset.Name, preset.SystemPrompt);
            var agent = await createAgent(
                name: preset.Name,
                systemPrompt: template,
                tools: [],
                endpoint: project.SystemEndpoint,
                project: project,
                modelParameters: createParams(temperature: 0.0),
                isSystemAgent: true).AddAsync(cancellationToken);

            var evaluator = createAgentic(agent);
            return (IAgenticEvaluator)await evaluatorRepo.AddAsync(evaluator, cancellationToken);
        }
    }
}
