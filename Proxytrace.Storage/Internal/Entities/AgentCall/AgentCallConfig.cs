using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Common.Async;
using Proxytrace.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Usage;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.AgentVersion;
using Proxytrace.Storage.Internal.Entities.Inference;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;

namespace Proxytrace.Storage.Internal.Entities.AgentCall;

internal class AgentCallConfig : AbstractEntityConfiguration<AgentCallEntity>, IMapper<IAgentCall, AgentCallEntity>
{
    private readonly IAgentCall.CreateExisting factory;
    private readonly IModelParameters.Create modelParametersFactory;
    private readonly ISerializer serializer;
    private readonly IRepository<IAgentVersion> versions;
    private readonly IRepository<IAgent> agents;
    private readonly ICompletion.Create completionFactory;
    private readonly IRepository<IModelEndpoint> endpoints;

    public AgentCallConfig(
        IAgentCall.CreateExisting factory,
        IModelParameters.Create modelParametersFactory,
        ISerializer serializer,
        IRepository<IAgentVersion> versions,
        IRepository<IAgent> agents,
        ICompletion.Create completionFactory,
        IRepository<IModelEndpoint> endpoints)
    {
        this.factory = factory;
        this.modelParametersFactory = modelParametersFactory;
        this.serializer = serializer;
        this.versions = versions;
        this.agents = agents;
        this.completionFactory = completionFactory;
        this.endpoints = endpoints;
    }

    public override void Configure(EntityTypeBuilder<AgentCallEntity> builder)
    {
        // Composite (AgentVersionId, CreatedAt): serves the agent/project-scoped trace list and
        // time-series (which filter by version then order/range by CreatedAt) in one index, and its
        // leading column still covers the agent-version foreign key.
        builder.HasIndex(e => new { e.AgentVersionId, e.CreatedAt });
        builder.HasIndex(e => e.EndpointId);

        builder
            .HasOne<AgentVersionEntity>()
            .WithMany()
            .HasForeignKey(e => e.AgentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.CreatedAt);
        builder.Property(e => e.FinishReason).HasMaxLength(64);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2048);
        builder.Property(e => e.RequestPreview).HasMaxLength(AgentCallPreview.MaxLength);
        builder.HasIndex(e => e.ConversationId);

        // Composite (SessionId, CreatedAt): the session detail page pages one session's traces
        // chronologically; the leading column alone also serves the traces-list session filter.
        builder.HasIndex(e => new { e.SessionId, e.CreatedAt });
        builder.HasIndex(e => e.LatencyMs);
        builder.HasIndex(e => e.TotalTokens);
        builder.HasIndex(e => e.ResponseToolRequestCount);
        builder.HasIndex(e => e.CacheHitRate);

        // Partial index serving the "outliers only" trace filter (WHERE OutlierFlags <> 0). Outliers
        // are a small fraction of rows, so a filtered index stays tiny and is the cheapest way to page
        // them on this high-volume table. The filter is relational metadata; the in-memory provider
        // (kiosk/tests) ignores indexes, so this is a no-op there.
        builder.HasIndex(e => e.OutlierFlags).HasFilter("\"OutlierFlags\" <> 0");

        builder
            .Property(e => e.Request)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.DeserializeRequired<Conversation>(v)
            );

        builder
            .Property(e => e.Response)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.DeserializeRequired<AssistantMessage>(v)
            );

        builder
            .Property(e => e.ModelParameters)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.Deserialize<ModelParametersData>(v) ?? ModelParametersData.Empty
            );

        // Restrict, not Cascade: AgentCall is the product's highest-volume table (irreplaceable
        // telemetry). A Cascade here let a single hard delete of a ModelEndpoint — or, transitively,
        // a ModelProvider — wipe every trace recorded against it. Endpoints/providers are removed via
        // the archive flow (ArchivableRepository), never hard-deleted, so Restrict blocks only the
        // accidental hard delete while leaving the supported path untouched. Mirrors the
        // AgentVersion -> AgentCall restriction above.
        builder
            .HasOne<ModelEndpointEntity>()
            .WithMany()
            .HasForeignKey(e => e.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public async Task<IAgentCall> Map(AgentCallEntity stored, CancellationToken cancellationToken = default)
    {
        IAgentVersion version = await versions.GetAsync(stored.AgentVersionId, cancellationToken);
        IAgent agent = await agents.GetAsync(version.AgentId, cancellationToken);
        IModelEndpoint endpoint = await endpoints.GetAsync(stored.EndpointId, cancellationToken);
        var completion =
            stored.Response is not null
                ? completionFactory(
                    stored.Response,
                    usage: TokenUsage.Create(stored.InputTokens, stored.OutputTokens, stored.CachedInputTokens),
                    latency: TimeSpan.FromMilliseconds(stored.LatencyMs ?? 0))
                : null;
        var modelParameters = AgentConfig.ToDomain(stored.ModelParameters, modelParametersFactory);
        return factory(
            agent: agent,
            version: version,
            endpoint: endpoint,
            request: stored.Request,
            response: completion,
            httpStatus: (HttpStatusCode)stored.HttpStatus,
            finishReason: stored.FinishReason,
            errorMessage: stored.ErrorMessage,
            modelParameters: modelParameters,
            existing: stored,
            conversationId: stored.ConversationId,
            sessionId: stored.SessionId,
            outlierFlags: stored.OutlierFlags);
    }

    public Task<AgentCallEntity> Map(IAgentCall domain, CancellationToken cancellationToken = default)
        => new AgentCallEntity
        {
            Id = domain.Id,
            AgentVersionId = domain.Version.Id,
            EndpointId = domain.Endpoint.Id,
            Request = domain.Request,
            Response = domain.Response?.Response,
            InputTokens = domain.Response?.Usage?.InputTokenCount,
            OutputTokens = domain.Response?.Usage?.OutputTokenCount,
            CachedInputTokens = domain.Response?.Usage?.CachedInputTokenCount,
            LatencyMs = domain.Response?.Latency.TotalMilliseconds,
            HttpStatus = (int)domain.HttpStatus,
            FinishReason = domain.FinishReason,
            ErrorMessage = domain.ErrorMessage,
            ModelParameters = AgentConfig.ToData(domain.ModelParameters),
            ConversationId = domain.ConversationId,
            SessionId = domain.SessionId,
            OutlierFlags = domain.OutlierFlags,
            RequestPreview = AgentCallPreview.Build(domain.Request),
            ResponseToolRequestCount = domain.Response?.Response is AssistantMessage assistant
                ? assistant.ToolRequests.Count
                : 0,
            TotalTokens = domain.Response?.Usage is { } u ? u.InputTokenCount + u.OutputTokenCount : null,
            CacheHitRate = domain.Response?.Usage is { InputTokenCount: > 0 } usage
                ? (double)usage.CachedInputTokenCount / usage.InputTokenCount
                : null,
            Tools = domain.Response?.Response is AssistantMessage msg
                ? msg.ToolRequests
                    .Select(t => t.Name)
                    .Distinct()
                    .Select(name => new AgentCallToolEntity
                    {
                        Id = Guid.NewGuid(),
                        AgentCallId = domain.Id,
                        ProjectId = domain.Version.ProjectId,
                        AgentId = domain.Version.AgentId,
                        ToolName = name,
                        CreatedAt = domain.CreatedAt,
                        UpdatedAt = domain.UpdatedAt,
                    })
                    .ToList()
                : [],
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
