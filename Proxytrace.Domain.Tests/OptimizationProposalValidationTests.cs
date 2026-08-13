using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class OptimizationProposalValidationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateNew_SystemPrompt_CreatesProposal()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(
            agent: agent,
            priority: Priority.High,
            rationale: "Tests failed due to vague instructions.",
            proposedSystemMessage: "Improved system prompt",
            currentPassRate: 0.5,
            proposedPassRate: 0.7,
            evidenceTestRunIds: [Guid.NewGuid()],
            abTestRun: abRun);

        proposal.Should().NotBeNull();
        proposal.Agent.Should().Be(agent);
        proposal.Kind.Should().Be(ProposalKind.SystemPrompt);
        proposal.Status.Should().Be(ProposalStatus.Draft);
        proposal.Priority.Should().Be(Priority.High);
        proposal.Rationale.Should().Be("Tests failed due to vague instructions.");
        proposal.ProposedSystemMessage.Should().Be("Improved system prompt");
        proposal.EvidenceTestRunIds.Should().ContainSingle();
        proposal.ABTestRun.Should().Be(abRun);
        proposal.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task CreateNew_ToolUpdate_KindIsTool()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(
            agent: agent,
            priority: Priority.Medium,
            rationale: "Tool arguments were misused.",
            proposedTools: [],
            currentPassRate: 0.5,
            proposedPassRate: 0.7,
            evidenceTestRunIds: [],
            abTestRun: abRun);

        proposal.Kind.Should().Be(ProposalKind.Tool);
        proposal.Status.Should().Be(ProposalStatus.Draft);
        proposal.ProposedTools.Should().BeEmpty();
        proposal.ABTestRun.Should().Be(abRun);
    }

    [TestMethod]
    public async Task CreateNew_ModelSwitch_KindIsModelSwitch()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(
            agent: agent,
            priority: Priority.Critical,
            rationale: "Switching model improves pass rate by 25%.",
            proposedEndpoint: endpoint,
            currentPassRate: 0.6,
            proposedPassRate: 0.85,
            expectedCostDelta: -0.05m,
            expectedLatencyDelta: TimeSpan.FromMilliseconds(-200),
            evidenceTestRunIds: [Guid.NewGuid(), Guid.NewGuid()],
            abTestRun: abRun);

        proposal.Kind.Should().Be(ProposalKind.ModelSwitch);
        proposal.ProposedEndpoint.Should().Be(endpoint);
        proposal.ExpectedPassRateDelta.Should().Be(0.25);
        proposal.ABTestRun.Should().Be(abRun);
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyRationale_ThrowsValidationException()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var action = () => factory(agent, Priority.Low, "   ", "msg", null, null, [], abRun);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_NewProposal_AlwaysStartsAsDraft()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Low, "rationale", [], null, null, [], abRun);

        proposal.Status.Should().Be(ProposalStatus.Draft);
    }

    [TestMethod]
    public async Task CreateNew_IdIsUniqueForEachProposal()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal1 = factory(agent, Priority.Low, "rationale", [], null, null, [], abRun);
        var proposal2 = factory(agent, Priority.Low, "rationale", [], null, null, [], abRun);

        proposal1.Id.Should().NotBe(proposal2.Id);
    }

    [TestMethod]
    public async Task CreateNew_WithMultipleEvidenceTestRunIds_TracksAllIds()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);
        var evidenceIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var proposal = factory(
            agent: agent,
            priority: Priority.Medium,
            rationale: "Multiple test runs failed.",
            proposedSystemMessage: "New prompt",
            currentPassRate: null,
            proposedPassRate: null,
            evidenceTestRunIds: evidenceIds,
            abTestRun: abRun);

        proposal.EvidenceTestRunIds.Should().HaveCount(3);
        proposal.EvidenceTestRunIds.Should().BeEquivalentTo(evidenceIds);
    }

    [TestMethod]
    public async Task CreateExisting_ReconstitutesSystemPromptProposalWithOriginalValues()
    {
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ISystemPromptProposal.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ISystemPromptProposal>>();
        var existing = await generator.CreateAsync(CancellationToken);

        var reconstituted = createExisting(
            agent: existing.Agent,
            status: existing.Status,
            priority: existing.Priority,
            rationale: existing.Rationale,
            proposedSystemMessage: existing.ProposedSystemMessage,
            currentPassRate: existing.CurrentPassRate,
            proposedPassRate: existing.ProposedPassRate,
            evidenceTestRunIds: existing.EvidenceTestRunIds,
            abTestRun: existing.ABTestRun,
            contentHash: existing.ContentHash,
            adoptedAt: existing.AdoptedAt,
            adoptedAgentVersionId: existing.AdoptedAgentVersionId,
            adoptedAgentVersionNumber: existing.AdoptedAgentVersionNumber,
            adoptedManually: existing.AdoptedManually,
            existing: existing);

        reconstituted.Id.Should().Be(existing.Id);
        reconstituted.Agent.Should().Be(existing.Agent);
        reconstituted.Kind.Should().Be(existing.Kind);
        reconstituted.Status.Should().Be(existing.Status);
        reconstituted.Priority.Should().Be(existing.Priority);
        reconstituted.Rationale.Should().Be(existing.Rationale);
        reconstituted.CreatedAt.Should().Be(existing.CreatedAt);
        reconstituted.UpdatedAt.Should().Be(existing.UpdatedAt);
    }

    [TestMethod]
    public async Task CreateExisting_CanReconstituteDraftAcceptedAndRejectedStatus()
    {
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ISystemPromptProposal.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ISystemPromptProposal>>();
        var source = await generator.CreateAsync(CancellationToken);

        var accepted = createExisting(source.Agent, ProposalStatus.Accepted, source.Priority, source.Rationale,
            source.ProposedSystemMessage, source.CurrentPassRate, source.ProposedPassRate, source.EvidenceTestRunIds, source.ABTestRun, source.ContentHash,
            null, null, null, null, source);
        var rejected = createExisting(source.Agent, ProposalStatus.Rejected, source.Priority, source.Rationale,
            source.ProposedSystemMessage, source.CurrentPassRate, source.ProposedPassRate, source.EvidenceTestRunIds, source.ABTestRun, source.ContentHash,
            null, null, null, null, source);

        accepted.Status.Should().Be(ProposalStatus.Accepted);
        rejected.Status.Should().Be(ProposalStatus.Rejected);
    }

    [TestMethod]
    public async Task ExpectedPassRateDelta_BothNull_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Low, "r", "msg", null, null, [], abRun);

        proposal.ExpectedPassRateDelta.Should().BeNull();
    }

    [TestMethod]
    public async Task ExpectedPassRateDelta_OnlyCurrentNull_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Low, "r", "msg", null, 0.8, [], abRun);

        proposal.ExpectedPassRateDelta.Should().BeNull();
    }

    [TestMethod]
    public async Task ExpectedPassRateDelta_OnlyProposedNull_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Low, "r", "msg", 0.6, null, [], abRun);

        proposal.ExpectedPassRateDelta.Should().BeNull();
    }

    [TestMethod]
    public async Task ExpectedPassRateDelta_BothSet_ReturnsDifference()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Low, "r", "msg", 0.6, 0.85, [], abRun);

        proposal.ExpectedPassRateDelta.Should().BeApproximately(0.25, 1e-9);
    }

    [TestMethod]
    public async Task ExpectedPassRateDelta_NegativeDelta_ReturnsNegative()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Low, "r", [], 0.9, 0.4, [], abRun);

        proposal.ExpectedPassRateDelta.Should().BeApproximately(-0.5, 1e-9);
    }

    [TestMethod]
    public async Task CreateNew_SystemPrompt_PreservesPassRates()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Medium, "r", "msg", 0.55, 0.72, [], abRun);

        proposal.CurrentPassRate.Should().Be(0.55);
        proposal.ProposedPassRate.Should().Be(0.72);
    }

    [TestMethod]
    public async Task CreateNew_ToolUpdate_PreservesPassRates()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Medium, "r", [], 0.40, 0.65, [], abRun);

        proposal.CurrentPassRate.Should().Be(0.40);
        proposal.ProposedPassRate.Should().Be(0.65);
    }

    [TestMethod]
    public async Task CreateNew_ModelSwitch_PreservesPassRates()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Medium, "r", endpoint, 0.30, 0.81, null, null, [], abRun);

        proposal.CurrentPassRate.Should().Be(0.30);
        proposal.ProposedPassRate.Should().Be(0.81);
        proposal.ExpectedPassRateDelta.Should().BeApproximately(0.51, 1e-9);
    }

    [TestMethod]
    public async Task CreateNew_SystemPrompt_WithProposedPassRateAboveOne_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var action = () => factory(agent, Priority.Low, "r", "msg", null, 1.7, [], abRun);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_SystemPrompt_WithNaNCurrentPassRate_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var action = () => factory(agent, Priority.Low, "r", "msg", double.NaN, null, [], abRun);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_ToolUpdate_WithNegativeProposedPassRate_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var action = () => factory(agent, Priority.Low, "r", [], 0.5, -0.2, [], abRun);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_ModelSwitch_WithProposedPassRateAboveOne_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var abRun = await CreateTestRunAsync(services);

        var action = () => factory(agent, Priority.Low, "r", endpoint, 0.6, 1.7, null, null, [], abRun);

        action.Should().Throw<Exception>();
    }

    private static async Task<IAgent> CreateAgentAsync(IServiceProvider services)
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        return await generator.GetOrCreateAsync();
    }

    private async Task<ITestRun> CreateTestRunAsync(IServiceProvider services)
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();
        return await generator.CreateAsync(CancellationToken);
    }
}
