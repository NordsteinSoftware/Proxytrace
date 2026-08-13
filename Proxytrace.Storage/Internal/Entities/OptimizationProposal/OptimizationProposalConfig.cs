using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.TestRun;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.AgentVersion;
using Proxytrace.Storage.Internal.Entities.TestRun;

namespace Proxytrace.Storage.Internal.Entities.OptimizationProposal;

internal class OptimizationProposalConfig :
    AbstractEntityConfiguration<OptimizationProposalEntity>,
    IMapper<IOptimizationProposal, OptimizationProposalEntity>
{
    private readonly IModelSwitchProposal.CreateExisting createModelSwitch;
    private readonly ISystemPromptProposal.CreateExisting createSystemPrompt;
    private readonly IToolUpdateProposal.CreateExisting createToolUpdate;
    private readonly ISerializer serializer;
    private readonly IRepository<IAgent> agents;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly IRepository<ITestRun> testRuns;

    public OptimizationProposalConfig(
        IModelSwitchProposal.CreateExisting createModelSwitch,
        ISystemPromptProposal.CreateExisting createSystemPrompt,
        IToolUpdateProposal.CreateExisting createToolUpdate,
        ISerializer serializer,
        IRepository<IAgent> agents,
        IRepository<IModelEndpoint> endpoints,
        IRepository<ITestRun> testRuns)
    {
        this.createModelSwitch = createModelSwitch;
        this.createSystemPrompt = createSystemPrompt;
        this.createToolUpdate = createToolUpdate;
        this.serializer = serializer;
        this.agents = agents;
        this.endpoints = endpoints;
        this.testRuns = testRuns;
    }

    public override void Configure(EntityTypeBuilder<OptimizationProposalEntity> builder)
    {
        builder
            .HasOne<AgentEntity>()
            .WithMany()
            .HasForeignKey(e => e.Agent)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<TestRunEntity>()
            .WithMany()
            .HasForeignKey(e => e.ABTestRun)
            // Cascade: a suite delete cascades suite -> run group -> test run, and this A/B run is one
            // of those. Restrict here blocked the whole suite delete (the proposal pinned the run).
            // Deleting the run that produced a proposal removes the proposal with it.
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull (not Restrict): versions cascade-delete with their agent, and the proposal
        // cascades from the same agent — a Restrict here would make agent deletion order-dependent.
        builder
            .HasOne<AgentVersionEntity>()
            .WithMany()
            .HasForeignKey(e => e.AdoptedAgentVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.ContentHash).HasMaxLength(64);

        builder.HasIndex(e => e.Agent);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Kind);
        builder.HasIndex(e => new { e.Agent, e.ContentHash });
    }

    public async Task<IOptimizationProposal> Map(OptimizationProposalEntity stored, CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetAsync(stored.Agent, cancellationToken);
        var abTestRun = await testRuns.GetAsync(stored.ABTestRun, cancellationToken);
        var evidenceTestRunIds = serializer.Deserialize<IReadOnlyCollection<Guid>>(stored.EvidenceTestRunIds)
                                 ?? [];

        return stored.Kind switch
        {
            ProposalKind.ModelSwitch => await MapModelSwitch(stored, agent, abTestRun, evidenceTestRunIds, cancellationToken),
            ProposalKind.SystemPrompt => MapSystemPrompt(stored, agent, abTestRun, evidenceTestRunIds),
            ProposalKind.Tool => MapToolUpdate(stored, agent, abTestRun, evidenceTestRunIds),
            _ => throw new ArgumentOutOfRangeException(nameof(stored.Kind))
        };
    }

    private async Task<IModelSwitchProposal> MapModelSwitch(
        OptimizationProposalEntity stored,
        IAgent agent,
        ITestRun abTestRun,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        CancellationToken cancellationToken)
    {
        var data = serializer.Deserialize<ModelSwitchProposalData>(stored.Data)
                   ?? throw new SerializationException($"Failed to deserialize ModelSwitchProposalData for proposal {stored.Id}");
        var proposedEndpoint = await endpoints.GetAsync(data.ProposedEndpointId, cancellationToken);
        return createModelSwitch(
            agent: agent,
            status: stored.Status,
            priority: stored.Priority,
            rationale: stored.Rationale,
            proposedEndpoint: proposedEndpoint,
            currentPassRate: stored.CurrentPassRate,
            proposedPassRate: stored.ProposedPassRate,
            expectedCostDelta: data.ExpectedCostDelta,
            expectedLatencyDelta: data.ExpectedLatencyDelta,
            evidenceTestRunIds: evidenceTestRunIds,
            abTestRun: abTestRun,
            contentHash: stored.ContentHash,
            adoptedAt: stored.AdoptedAt,
            adoptedAgentVersionId: stored.AdoptedAgentVersionId,
            adoptedAgentVersionNumber: stored.AdoptedAgentVersionNumber,
            adoptedManually: stored.AdoptedManually,
            existing: stored);
    }

    private ISystemPromptProposal MapSystemPrompt(
        OptimizationProposalEntity stored,
        IAgent agent,
        ITestRun abTestRun,
        IReadOnlyCollection<Guid> evidenceTestRunIds)
    {
        var data = serializer.Deserialize<SystemPromptProposalData>(stored.Data)
                   ?? throw new SerializationException($"Failed to deserialize SystemPromptProposalData for proposal {stored.Id}");
        return createSystemPrompt(
            agent: agent,
            status: stored.Status,
            priority: stored.Priority,
            rationale: stored.Rationale,
            proposedSystemMessage: data.ProposedSystemMessage,
            currentPassRate: stored.CurrentPassRate,
            proposedPassRate: stored.ProposedPassRate,
            evidenceTestRunIds: evidenceTestRunIds,
            abTestRun: abTestRun,
            contentHash: stored.ContentHash,
            adoptedAt: stored.AdoptedAt,
            adoptedAgentVersionId: stored.AdoptedAgentVersionId,
            adoptedAgentVersionNumber: stored.AdoptedAgentVersionNumber,
            adoptedManually: stored.AdoptedManually,
            existing: stored);
    }

    private IToolUpdateProposal MapToolUpdate(
        OptimizationProposalEntity stored,
        IAgent agent,
        ITestRun abTestRun,
        IReadOnlyCollection<Guid> evidenceTestRunIds)
    {
        var data = serializer.Deserialize<ToolUpdateProposalData>(stored.Data)
                   ?? throw new SerializationException($"Failed to deserialize ToolUpdateProposalData for proposal {stored.Id}");
        return createToolUpdate(
            agent: agent,
            status: stored.Status,
            priority: stored.Priority,
            rationale: stored.Rationale,
            proposedTools: data.ProposedTools,
            currentPassRate: stored.CurrentPassRate,
            proposedPassRate: stored.ProposedPassRate,
            evidenceTestRunIds: evidenceTestRunIds,
            abTestRun: abTestRun,
            contentHash: stored.ContentHash,
            adoptedAt: stored.AdoptedAt,
            adoptedAgentVersionId: stored.AdoptedAgentVersionId,
            adoptedAgentVersionNumber: stored.AdoptedAgentVersionNumber,
            adoptedManually: stored.AdoptedManually,
            existing: stored);
    }

    public Task<OptimizationProposalEntity> Map(IOptimizationProposal domain, CancellationToken cancellationToken = default)
    {
        string data = domain switch
        {
            IModelSwitchProposal ms => serializer.Serialize(new ModelSwitchProposalData(
                ms.ProposedEndpoint.Id,
                ms.ExpectedCostDelta,
                ms.ExpectedLatencyDelta)),
            ISystemPromptProposal sp => serializer.Serialize(new SystemPromptProposalData(sp.ProposedSystemMessage)),
            IToolUpdateProposal tu => serializer.Serialize(new ToolUpdateProposalData(tu.ProposedTools)),
            _ => throw new NotSupportedException($"Unsupported proposal type: {domain.GetType()}")
        };

        return new OptimizationProposalEntity
        {
            Id = domain.Id,
            Agent = domain.Agent.Id,
            Kind = domain.Kind,
            Status = domain.Status,
            Priority = domain.Priority,
            Rationale = domain.Rationale,
            ABTestRun = domain.ABTestRun.Id,
            Data = data,
            EvidenceTestRunIds = serializer.Serialize(domain.EvidenceTestRunIds),
            CurrentPassRate = domain.CurrentPassRate,
            ProposedPassRate = domain.ProposedPassRate,
            ContentHash = domain.ContentHash,
            AdoptedAt = domain.AdoptedAt,
            AdoptedAgentVersionId = domain.AdoptedAgentVersionId,
            AdoptedAgentVersionNumber = domain.AdoptedAgentVersionNumber,
            AdoptedManually = domain.AdoptedManually,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
    }
}
