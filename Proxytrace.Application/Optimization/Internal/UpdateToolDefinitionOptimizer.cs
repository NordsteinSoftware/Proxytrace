using System.ComponentModel;
using JetBrains.Annotations;
using Proxytrace.Application.Optimization.Internal.Evidence;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationTheory;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.Optimization.Internal;

[UsedImplicitly]
internal sealed class UpdateToolDefinitionOptimizer : IOptimizerImplementation
{
    internal const string PromptName = "update_tool_definition_optimizer";

    private readonly IToolUpdateTheory.CreateNew factory;
    private readonly IPromptTemplateRepository prompts;
    private readonly IAgentRepository agents;
    private readonly IOptimizerEvidenceBuilder evidenceBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateToolDefinitionOptimizer"/> class.
    /// </summary>
    public UpdateToolDefinitionOptimizer(
        IToolUpdateTheory.CreateNew factory,
        IPromptTemplateRepository prompts,
        IAgentRepository agents,
        IOptimizerEvidenceBuilder evidenceBuilder)
    {
        this.factory = factory;
        this.prompts = prompts;
        this.agents = agents;
        this.evidenceBuilder = evidenceBuilder;
    }

    /// <summary>
    /// Discover theories.
    /// </summary>
    public async Task<IReadOnlyList<IOptimizationTheory>> DiscoverTheories(
        ITestRunGroup testRunGroup,
        IReadOnlyList<RunCohort> cohorts,
        CancellationToken cancellationToken = default)
    {
        var agent = testRunGroup.Suite.Agent;
        if (agent.Tools.Count == 0)
        {
            return [];
        }

        var cohort = cohorts.FirstOrDefault(c => c.EndpointId == agent.Endpoint.Id);
        if (cohort is null)
        {
            return [];
        }

        // Aggregated stats across the endpoint's samples; gate on a non-zero failure count.
        TestRunStats? stats = cohort.Stats;
        if (stats is null || stats.Failed == 0)
        {
            return [];
        }

        ITestRun currentRun = cohort.Representative;

        IPromptTemplate systemPrompt = await prompts.GetAsync(PromptName, cancellationToken);
        IAgent optimizer = await agents.GetOrCreateAsync(
            systemPrompt: systemPrompt,
            tools: [],
            project: agent.Project,
            endpoint: agent.Project.SystemEndpoint,
            name: PromptName,
            isSystemAgent: true,
            cancellationToken: cancellationToken);

        OptimizerEvidence evidence = evidenceBuilder.Build(currentRun);
        using var optimizerClient = optimizer.CreateClient();
        ToolOptimizerOutput? completion = await optimizerClient
            .CompleteAsync<ToolOptimizerOutput>(
                Message.CreateUserMessage(evidence.ToJson()),
                cancellationToken: cancellationToken);

        if (completion is null)
        {
            return [];
        }

        if (completion.Tools.Count != agent.Tools.Count)
        {
            return [];
        }

        var existingNames = agent.Tools.Select(t => t.Name).ToHashSet();
        if (completion.Tools.Any(t => !existingNames.Contains(t.Name)))
        {
            return [];
        }

        IReadOnlyList<ToolSpecification> proposedTools;
        try
        {
            proposedTools = completion.Tools
                .Select(t => new ToolSpecification(
                    t.Name,
                    t.Description,
                    ToolArguments.FromJsonSchema(t.JsonSchema)))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }

        Priority priority = stats.GetOptimizationPriority();
        string fullRationale =
            $"{completion.Rationale} (Current run: {stats.Failed}/{stats.TestCases} failed.)";

        var theory = factory(
            agent: agent,
            suite: testRunGroup.Suite,
            source: TheorySource.Optimizer,
            priority: priority,
            rationale: fullRationale,
            proposedTools: proposedTools,
            evidenceTestRunIds: [currentRun.Id]);

        return [theory];
    }

    [UsedImplicitly]
    private record ToolOptimizerOutput
    {
        /// <summary>
        /// Gets or sets the tools.
        /// </summary>
        [Description("the tool definitions that should be changed")]
        public required IReadOnlyList<ProposedTool> Tools { get; [UsedImplicitly] init; }

        /// <summary>
        /// Gets or sets the rationale.
        /// </summary>
        [Description("1-3 sentences explaining what changed and why")]
        public required string Rationale { get; [UsedImplicitly] init; }
    }

    [UsedImplicitly]
    private record ProposedTool
    {
        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        [Description("the tool name; must match an existing tool name on the agent")]
        public required string Name { get; [UsedImplicitly] init; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        [Description("the proposed tool description")]
        public required string Description { get; [UsedImplicitly] init; }

        /// <summary>
        /// Gets or sets the json schema.
        /// </summary>
        [Description("the proposed JSON schema for the tool's arguments, as a JSON-encoded string (a JSON object with 'type', 'properties', and 'required' fields)")]
        public required string JsonSchema { get; [UsedImplicitly] init; }
    }
}
