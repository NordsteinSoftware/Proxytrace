using System.Text.RegularExpressions;
using Proxytrace.Api.Dto.Evaluators;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;

namespace Proxytrace.Api.Evaluators;

/// <summary>
/// Builds <see cref="IEvaluator"/> instances from per-kind request DTOs.
/// Dispatch happens via pattern matching on the polymorphic request type;
/// validation of subtype-specific fields is enforced by the DTOs themselves
/// (required init properties + STJ polymorphism), so this layer only handles
/// the domain-construction step.
/// </summary>
public sealed class EvaluatorBuilder
{
    private readonly IAgent.CreateNew createAgent;
    private readonly IModelParameters.Create createModelParameters;
    private readonly IPromptTemplate.Create createPromptTemplate;
    private readonly IAgenticEvaluator.CreateNew createAgentic;
    private readonly IAgenticEvaluator.CreateExisting createAgenticExisting;
    private readonly IExactMatchEvaluator.CreateNew createExactMatch;
    private readonly IExactMatchEvaluator.CreateExisting createExactMatchExisting;
    private readonly INumericMatchEvaluator.CreateNew createNumericMatch;
    private readonly INumericMatchEvaluator.CreateExisting createNumericMatchExisting;
    private readonly IJsonSchemaMatchEvaluator.CreateNew createJsonSchemaMatch;
    private readonly IJsonSchemaMatchEvaluator.CreateExisting createJsonSchemaMatchExisting;
    private readonly ILicenseService licenseService;

    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluatorBuilder"/> class.
    /// </summary>
    public EvaluatorBuilder(
        IAgent.CreateNew createAgent,
        IModelParameters.Create createModelParameters,
        IPromptTemplate.Create createPromptTemplate,
        IAgenticEvaluator.CreateNew createAgentic,
        IAgenticEvaluator.CreateExisting createAgenticExisting,
        IExactMatchEvaluator.CreateNew createExactMatch,
        IExactMatchEvaluator.CreateExisting createExactMatchExisting,
        INumericMatchEvaluator.CreateNew createNumericMatch,
        INumericMatchEvaluator.CreateExisting createNumericMatchExisting,
        IJsonSchemaMatchEvaluator.CreateNew createJsonSchemaMatch,
        IJsonSchemaMatchEvaluator.CreateExisting createJsonSchemaMatchExisting,
        ILicenseService licenseService)
    {
        this.createAgent = createAgent;
        this.createModelParameters = createModelParameters;
        this.createPromptTemplate = createPromptTemplate;
        this.createAgentic = createAgentic;
        this.createAgenticExisting = createAgenticExisting;
        this.createExactMatch = createExactMatch;
        this.createExactMatchExisting = createExactMatchExisting;
        this.createNumericMatch = createNumericMatch;
        this.createNumericMatchExisting = createNumericMatchExisting;
        this.createJsonSchemaMatch = createJsonSchemaMatch;
        this.createJsonSchemaMatchExisting = createJsonSchemaMatchExisting;
        this.licenseService = licenseService;
    }

    private void EnsureAgenticLicensed()
    {
        if (!licenseService.IsFeatureEnabled(LicenseFeature.AgenticEvaluators))
            throw new FeatureNotLicensedException(LicenseFeature.AgenticEvaluators, licenseService.Current.Tier);
    }

    /// <summary>
    /// Builds asynchronously.
    /// </summary>
    public Task<IEvaluator> BuildAsync(
        CreateEvaluatorRequest request,
        IProject project,
        CancellationToken cancellationToken) => request switch
    {
        CreateAgenticEvaluatorRequest r => BuildAgenticAsync(r, project, cancellationToken),
        CreateExactMatchEvaluatorRequest => Task.FromResult<IEvaluator>(createExactMatch(project)),
        CreateNumericMatchEvaluatorRequest r => Task.FromResult<IEvaluator>(
            createNumericMatch(new Regex(r.ExtractionPattern), r.Tolerance, project)),
        CreateJsonSchemaMatchEvaluatorRequest r => Task.FromResult<IEvaluator>(
            createJsonSchemaMatch(r.JsonSchema, project)),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.GetType().Name),
    };

    /// <summary>
    /// Builds asynchronously.
    /// </summary>
    public Task<IEvaluator> BuildAsync(
        UpdateEvaluatorRequest request,
        IEvaluator existing,
        CancellationToken cancellationToken) => (request, existing) switch
    {
        (UpdateAgenticEvaluatorRequest r, IAgenticEvaluator current) =>
            UpdateAgenticAsync(r, current, cancellationToken),
        (UpdateExactMatchEvaluatorRequest, IExactMatchEvaluator current) =>
            Task.FromResult<IEvaluator>(createExactMatchExisting(current.Project, current)),
        (UpdateNumericMatchEvaluatorRequest r, INumericMatchEvaluator current) =>
            Task.FromResult<IEvaluator>(createNumericMatchExisting(
                new Regex(r.ExtractionPattern ?? current.ExtractionPattern.ToString()),
                r.Tolerance ?? current.Tolerance,
                current.Project,
                current)),
        (UpdateJsonSchemaMatchEvaluatorRequest r, IJsonSchemaMatchEvaluator current) =>
            Task.FromResult<IEvaluator>(createJsonSchemaMatchExisting(
                r.JsonSchema ?? current.JsonSchema,
                current.Project,
                current)),
        _ => throw new ArgumentException(
            $"Update request {request.GetType().Name} does not match existing evaluator kind {existing.Kind}.",
            nameof(request)),
    };

    private async Task<IEvaluator> BuildAgenticAsync(
        CreateAgenticEvaluatorRequest request,
        IProject project,
        CancellationToken cancellationToken)
    {
        EnsureAgenticLicensed();

        var prompt = createPromptTemplate(request.Name, request.SystemMessage);
        var agent = createAgent(
            name: request.Name,
            prompt,
            tools: [],
            endpoint: project.SystemEndpoint,
            project: project,
            modelParameters: createModelParameters(),
            isSystemAgent: true);
        agent = await agent.AddAsync(cancellationToken);
        return createAgentic(agent);
    }

    private async Task<IEvaluator> UpdateAgenticAsync(
        UpdateAgenticEvaluatorRequest request,
        IAgenticEvaluator current,
        CancellationToken cancellationToken)
    {
        EnsureAgenticLicensed();

        var name = request.Name ?? current.Name;
        var template = request.SystemMessage ?? current.Agent.SystemPrompt.Template;
        var prompt = createPromptTemplate(name, template);
        var agent = await current.Agent.CreateNewVersionAsync(prompt, current.Agent.Tools, cancellationToken);
        return createAgenticExisting(agent, current);
    }
}
