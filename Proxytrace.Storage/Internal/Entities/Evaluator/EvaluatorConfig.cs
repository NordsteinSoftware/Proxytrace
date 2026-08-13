using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;

namespace Proxytrace.Storage.Internal.Entities.Evaluator;

internal class EvaluatorConfig : AbstractEntityConfiguration<EvaluatorEntity>, IMapper<IEvaluator, EvaluatorEntity>
{
    private readonly IExactMatchEvaluator.CreateExisting createExactMatch;
    private readonly IAgenticEvaluator.CreateExisting createAgentic;
    private readonly IJsonSchemaMatchEvaluator.CreateExisting createJsonSchemaMatch;
    private readonly INumericMatchEvaluator.CreateExisting createNumericMatch;
    private readonly IRepository<IAgent> agents;
    private readonly IRepository<IProject> projects;
    private readonly ISerializer serializer;

    public EvaluatorConfig(
        IExactMatchEvaluator.CreateExisting createExactMatch,
        IAgenticEvaluator.CreateExisting createAgentic,
        IJsonSchemaMatchEvaluator.CreateExisting createJsonSchemaMatch,
        INumericMatchEvaluator.CreateExisting createNumericMatch,
        IRepository<IModelEndpoint> modelEndpoints,
        IRepository<IAgent> agents,
        IRepository<IProject> projects,
        ISerializer serializer)
    {
        this.createExactMatch = createExactMatch;
        this.createAgentic = createAgentic;
        this.createJsonSchemaMatch = createJsonSchemaMatch;
        this.createNumericMatch = createNumericMatch;
        this.agents = agents;
        this.projects = projects;
        this.serializer = serializer;
    }

    public override void Configure(EntityTypeBuilder<EvaluatorEntity> builder)
    {
        builder.HasIndex(e => e.Kind);
        // Composite supports the project-scoped list query, which excludes archived rows.
        builder.HasIndex(e => new { e.Project, e.IsArchived });
    }

    public async Task<IEvaluator> Map(EvaluatorEntity stored, CancellationToken cancellationToken = default)
    {
        IProject project = await projects.GetAsync(stored.Project, cancellationToken);
        return stored.Kind switch
        {
            EvaluatorKind.ExactMatch => createExactMatch(project, stored),
            EvaluatorKind.Agentic => await MapAgentic(stored, cancellationToken),
            EvaluatorKind.JsonSchemaMatch => MapJsonSchemaMatch(stored, project),
            EvaluatorKind.NumericMatch => MapNumericMatch(stored, project),
            _ => throw new InvalidOperationException($"Unknown evaluator kind: {stored.Kind}")
        };
    }
    
    private async Task<IAgenticEvaluator> MapAgentic(EvaluatorEntity stored, CancellationToken cancellationToken)
    {
        var data = serializer.DeserializeRequired<AgenticEvaluatorData>(stored.Data);
        var agent = await agents.GetAsync(data.AgentId, cancellationToken);
        return createAgentic(agent, stored);
    }

    private IJsonSchemaMatchEvaluator MapJsonSchemaMatch(EvaluatorEntity stored, IProject project)
    {
        var data = serializer.DeserializeRequired<JsonSchemaMatchEvaluatorData>(stored.Data);
        return createJsonSchemaMatch(data.JsonSchema, project, stored);
    }

    private INumericMatchEvaluator MapNumericMatch(EvaluatorEntity stored, IProject project)
    {
        var data = serializer.DeserializeRequired<NumericMatchEvaluatorData>(stored.Data);
        return createNumericMatch(new Regex(data.ExtractionPattern), data.Tolerance, project, stored);
    }

    public Task<EvaluatorEntity> Map(IEvaluator domain, CancellationToken cancellationToken = default)
    {
        string data = domain switch
        {
            IAgenticEvaluator agentic =>
                serializer.Serialize(new AgenticEvaluatorData(agentic.Agent.Id)),
            IExactMatchEvaluator =>
                serializer.Serialize(new ExactMatchEvaluatorData()),
            IJsonSchemaMatchEvaluator jsonSchema =>
                serializer.Serialize(new JsonSchemaMatchEvaluatorData(jsonSchema.JsonSchema)),
            INumericMatchEvaluator numeric =>
                serializer.Serialize(new NumericMatchEvaluatorData(numeric.ExtractionPattern.ToString(), numeric.Tolerance)),
            _ => throw new NotSupportedException($"Unsupported evaluator type: {domain.GetType()}")
        };

        return new EvaluatorEntity
        {
            Id = domain.Id,
            Kind = domain.Kind,
            Data = data,
            Project = domain.Project.Id,
            IsArchived = domain.IsArchived,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
    }
}
