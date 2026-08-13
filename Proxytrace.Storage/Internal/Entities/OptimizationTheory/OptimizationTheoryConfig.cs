using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.OptimizationProposal;
using Proxytrace.Storage.Internal.Entities.TestRun;
using Proxytrace.Storage.Internal.Entities.TestSuite;

namespace Proxytrace.Storage.Internal.Entities.OptimizationTheory;

internal class OptimizationTheoryConfig :
    AbstractEntityConfiguration<OptimizationTheoryEntity>,
    IMapper<IOptimizationTheory, OptimizationTheoryEntity>
{
    private readonly IModelSwitchTheory.CreateExisting createModelSwitch;
    private readonly ISystemPromptTheory.CreateExisting createSystemPrompt;
    private readonly IToolUpdateTheory.CreateExisting createToolUpdate;
    private readonly ISerializer serializer;
    private readonly IRepository<IAgent> agents;
    private readonly IRepository<ITestSuite> suites;
    private readonly IRepository<IModelEndpoint> endpoints;

    public OptimizationTheoryConfig(
        IModelSwitchTheory.CreateExisting createModelSwitch,
        ISystemPromptTheory.CreateExisting createSystemPrompt,
        IToolUpdateTheory.CreateExisting createToolUpdate,
        ISerializer serializer,
        IRepository<IAgent> agents,
        IRepository<ITestSuite> suites,
        IRepository<IModelEndpoint> endpoints)
    {
        this.createModelSwitch = createModelSwitch;
        this.createSystemPrompt = createSystemPrompt;
        this.createToolUpdate = createToolUpdate;
        this.serializer = serializer;
        this.agents = agents;
        this.suites = suites;
        this.endpoints = endpoints;
    }

    public override void Configure(EntityTypeBuilder<OptimizationTheoryEntity> builder)
    {
        builder
            .HasOne<AgentEntity>()
            .WithMany()
            .HasForeignKey(e => e.Agent)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<TestSuiteEntity>()
            .WithMany()
            .HasForeignKey(e => e.Suite)
            // Cascade: deleting a suite removes the theories generated against it. The suite is the
            // theory's reason to exist; without it the theory is orphaned. See AbTestRun below.
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<OptimizationProposalEntity>()
            .WithMany()
            .HasForeignKey(e => e.ResultingProposalId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne<TestRunEntity>()
            .WithMany()
            .HasForeignKey(e => e.ABTestRunId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.ContentHash).HasMaxLength(64);

        builder.HasIndex(e => e.Agent);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Kind);
        builder.HasIndex(e => new { e.Agent, e.ContentHash });
    }

    public async Task<IOptimizationTheory> Map(OptimizationTheoryEntity stored, CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetAsync(stored.Agent, cancellationToken);
        var suite = await suites.GetAsync(stored.Suite, cancellationToken);
        var evidenceTestRunIds = serializer.Deserialize<IReadOnlyCollection<Guid>>(stored.EvidenceTestRunIds)
                                 ?? [];

        return stored.Kind switch
        {
            ProposalKind.ModelSwitch => await MapModelSwitch(stored, agent, suite, evidenceTestRunIds, cancellationToken),
            ProposalKind.SystemPrompt => MapSystemPrompt(stored, agent, suite, evidenceTestRunIds),
            ProposalKind.Tool => MapToolUpdate(stored, agent, suite, evidenceTestRunIds),
            _ => throw new ArgumentOutOfRangeException(nameof(stored.Kind))
        };
    }

    private async Task<IModelSwitchTheory> MapModelSwitch(
        OptimizationTheoryEntity stored,
        IAgent agent,
        ITestSuite suite,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        CancellationToken cancellationToken)
    {
        var data = serializer.Deserialize<ModelSwitchProposalData>(stored.Data)
                   ?? throw new SerializationException($"Failed to deserialize ModelSwitchProposalData for theory {stored.Id}");
        var proposedEndpoint = await endpoints.GetAsync(data.ProposedEndpointId, cancellationToken);
        return createModelSwitch(
            agent: agent,
            suite: suite,
            status: stored.Status,
            source: stored.Source,
            priority: stored.Priority,
            rationale: stored.Rationale,
            proposedEndpoint: proposedEndpoint,
            evidenceTestRunIds: evidenceTestRunIds,
            resultingProposalId: stored.ResultingProposalId,
            baselinePassRate: stored.BaselinePassRate,
            projectedPassRate: stored.ProjectedPassRate,
            pValue: stored.PValue,
            abTestRunId: stored.ABTestRunId,
            contentHash: stored.ContentHash,
            existing: stored);
    }

    private ISystemPromptTheory MapSystemPrompt(
        OptimizationTheoryEntity stored,
        IAgent agent,
        ITestSuite suite,
        IReadOnlyCollection<Guid> evidenceTestRunIds)
    {
        var data = serializer.Deserialize<SystemPromptProposalData>(stored.Data)
                   ?? throw new SerializationException($"Failed to deserialize SystemPromptProposalData for theory {stored.Id}");
        return createSystemPrompt(
            agent: agent,
            suite: suite,
            status: stored.Status,
            source: stored.Source,
            priority: stored.Priority,
            rationale: stored.Rationale,
            proposedSystemMessage: data.ProposedSystemMessage,
            evidenceTestRunIds: evidenceTestRunIds,
            resultingProposalId: stored.ResultingProposalId,
            baselinePassRate: stored.BaselinePassRate,
            projectedPassRate: stored.ProjectedPassRate,
            pValue: stored.PValue,
            abTestRunId: stored.ABTestRunId,
            contentHash: stored.ContentHash,
            existing: stored);
    }

    private IToolUpdateTheory MapToolUpdate(
        OptimizationTheoryEntity stored,
        IAgent agent,
        ITestSuite suite,
        IReadOnlyCollection<Guid> evidenceTestRunIds)
    {
        var data = serializer.Deserialize<ToolUpdateProposalData>(stored.Data)
                   ?? throw new SerializationException($"Failed to deserialize ToolUpdateProposalData for theory {stored.Id}");
        return createToolUpdate(
            agent: agent,
            suite: suite,
            status: stored.Status,
            source: stored.Source,
            priority: stored.Priority,
            rationale: stored.Rationale,
            proposedTools: data.ProposedTools,
            evidenceTestRunIds: evidenceTestRunIds,
            resultingProposalId: stored.ResultingProposalId,
            baselinePassRate: stored.BaselinePassRate,
            projectedPassRate: stored.ProjectedPassRate,
            pValue: stored.PValue,
            abTestRunId: stored.ABTestRunId,
            contentHash: stored.ContentHash,
            existing: stored);
    }

    public Task<OptimizationTheoryEntity> Map(IOptimizationTheory domain, CancellationToken cancellationToken = default)
    {
        string data = domain switch
        {
            IModelSwitchTheory ms => serializer.Serialize(new ModelSwitchProposalData(
                ms.ProposedEndpoint.Id,
                null,
                null)),
            ISystemPromptTheory sp => serializer.Serialize(new SystemPromptProposalData(sp.ProposedSystemMessage)),
            IToolUpdateTheory tu => serializer.Serialize(new ToolUpdateProposalData(tu.ProposedTools)),
            _ => throw new NotSupportedException($"Unsupported theory type: {domain.GetType()}")
        };

        return new OptimizationTheoryEntity
        {
            Id = domain.Id,
            Agent = domain.Agent.Id,
            Suite = domain.Suite.Id,
            Kind = domain.Kind,
            Status = domain.Status,
            Source = domain.Source,
            Priority = domain.Priority,
            Rationale = domain.Rationale,
            Data = data,
            EvidenceTestRunIds = serializer.Serialize(domain.EvidenceTestRunIds),
            ResultingProposalId = domain.ResultingProposalId,
            BaselinePassRate = domain.BaselinePassRate,
            ProjectedPassRate = domain.ProjectedPassRate,
            PValue = domain.PValue,
            ABTestRunId = domain.ABTestRunId,
            ContentHash = domain.ContentHash,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
    }
}
