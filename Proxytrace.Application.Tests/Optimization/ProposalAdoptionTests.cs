using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Optimization.Internal.Adoption;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Optimization;

[TestClass]
public sealed class ProposalAdoptionTests : BaseTest<Module>
{
    // ── Matcher ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MatchesVersion_PromptProposal_ExactPromptMatches()
    {
        IServiceProvider services = GetServices();
        var matcher = services.GetRequiredService<ProposalAdoptionMatcher>();
        var proposal = await SeedPromptProposalAsync(services, "You are concise.");
        var matching = await CreateVersionAsync(services, "You are concise.", []);
        var different = await CreateVersionAsync(services, "You are concise!", []);

        matcher.MatchesVersion(proposal, matching).Should().BeTrue();
        matcher.MatchesVersion(proposal, different).Should().BeFalse();
    }

    [TestMethod]
    public async Task MatchesVersion_ToolProposal_SetEqualityIsOrderInsensitiveButExact()
    {
        IServiceProvider services = GetServices();
        var matcher = services.GetRequiredService<ProposalAdoptionMatcher>();
        var toolA = new ToolSpecification("alpha", "first tool", ToolArguments.None);
        var toolB = new ToolSpecification("beta", "second tool", ToolArguments.None);
        var proposal = await SeedToolProposalAsync(services, [toolA, toolB]);

        var reordered = await CreateVersionAsync(services, "p", [toolB, toolA]);
        var tweakedDescription = await CreateVersionAsync(
            services, "p", [toolA, new ToolSpecification("beta", "second tool, improved", ToolArguments.None)]);
        var missingTool = await CreateVersionAsync(services, "p", [toolA]);

        matcher.MatchesVersion(proposal, reordered).Should().BeTrue();
        matcher.MatchesVersion(proposal, tweakedDescription).Should().BeFalse();
        matcher.MatchesVersion(proposal, missingTool).Should().BeFalse();
    }

    [TestMethod]
    public async Task MatchesEndpoint_ModelSwitchProposal_MatchesOnlyProposedEndpoint()
    {
        IServiceProvider services = GetServices();
        var matcher = services.GetRequiredService<ProposalAdoptionMatcher>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var otherEndpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var proposingCurrent = await SeedModelSwitchProposalAsync(services, agent, agent.Endpoint);
        var proposingOther = await SeedModelSwitchProposalAsync(services, agent, otherEndpoint);

        matcher.MatchesEndpoint(proposingCurrent, agent).Should().BeTrue();
        matcher.MatchesEndpoint(proposingOther, agent).Should().BeFalse();
    }

    // ── Adoption service ────────────────────────────────────────────────────

    [TestMethod]
    public async Task PromptProposal_WhenMatchingVersionAppears_AutoAdoptsAndLinksVersion()
    {
        var broadcaster = Substitute.For<IProposalBroadcaster>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(broadcaster).As<IProposalBroadcaster>());

        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var proposal = await SeedPromptProposalAsync(services, "Adopted prompt text.", agent);
        await proposal.Accept(CancellationToken);

        var service = services.GetRequiredService<ProposalAdoptionService>();
        await service.StartAsync(CancellationToken);
        try
        {
            var template = services.GetRequiredService<IPromptTemplate.Create>()("n", "Adopted prompt text.");
            await agent.ChangeSystemMessage(template, CancellationToken);

            var adopted = await WaitForStatusAsync(services, proposal.Id, ProposalStatus.Adopted);

            adopted.Status.Should().Be(ProposalStatus.Adopted);
            adopted.AdoptedManually.Should().BeFalse();
            adopted.AdoptedAgentVersionId.Should().NotBeNull();
            adopted.AdoptedAgentVersionNumber.Should().NotBeNull();
            broadcaster.Received().Publish(Arg.Is<ProposalEvent>(e =>
                e is ProposalStatusChangedEvent && ((ProposalStatusChangedEvent)e).Status == ProposalStatus.Adopted));
        }
        finally
        {
            await service.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task PromptProposal_PromotedWhenAgentAlreadyRunsProposedPrompt_AdoptsImmediately()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var proposal = await SeedPromptProposalAsync(services, agent.SystemPrompt.Template, agent);

        var service = services.GetRequiredService<ProposalAdoptionService>();
        await service.StartAsync(CancellationToken);
        try
        {
            await proposal.Accept(CancellationToken);

            var adopted = await WaitForStatusAsync(services, proposal.Id, ProposalStatus.Adopted);

            adopted.Status.Should().Be(ProposalStatus.Adopted);
            adopted.AdoptedManually.Should().BeFalse();
        }
        finally
        {
            await service.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task ModelSwitchProposal_WhenAgentMovesToProposedEndpoint_AutoAdoptsWithoutVersion()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var proposedEndpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);
        var proposal = await SeedModelSwitchProposalAsync(services, agent, proposedEndpoint);
        await proposal.Accept(CancellationToken);

        var service = services.GetRequiredService<ProposalAdoptionService>();
        await service.StartAsync(CancellationToken);
        try
        {
            await agent.ChangeEndpoint(proposedEndpoint, CancellationToken);

            var adopted = await WaitForStatusAsync(services, proposal.Id, ProposalStatus.Adopted);

            adopted.Status.Should().Be(ProposalStatus.Adopted);
            adopted.AdoptedManually.Should().BeFalse();
            adopted.AdoptedAgentVersionId.Should().BeNull();
        }
        finally
        {
            await service.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task PromptProposal_WhenNonMatchingVersionAppears_StaysAccepted()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var proposal = await SeedPromptProposalAsync(services, "Proposed prompt.", agent);
        await proposal.Accept(CancellationToken);

        var service = services.GetRequiredService<ProposalAdoptionService>();
        await service.StartAsync(CancellationToken);
        try
        {
            var template = services.GetRequiredService<IPromptTemplate.Create>()("n", "A tweaked variant of the prompt.");
            await agent.ChangeSystemMessage(template, CancellationToken);

            // Give the service a moment to process the version event, then confirm no adoption.
            await Task.Delay(300, CancellationToken);
            var reloaded = await Reload(services, proposal.Id);
            reloaded.Status.Should().Be(ProposalStatus.Accepted);
        }
        finally
        {
            await service.StopAsync(CancellationToken);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<IOptimizationProposal> SeedPromptProposalAsync(
        IServiceProvider services, string proposedPrompt, IAgent? agent = null)
    {
        agent ??= await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        return await repo.AddAsync(
            factory(agent, Priority.Medium, "r", proposedPrompt, null, null, [], abRun),
            CancellationToken);
    }

    private async Task<IOptimizationProposal> SeedToolProposalAsync(
        IServiceProvider services, IReadOnlyList<ToolSpecification> proposedTools)
    {
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        return await repo.AddAsync(
            factory(agent, Priority.Medium, "r", proposedTools, null, null, [], abRun),
            CancellationToken);
    }

    private async Task<IOptimizationProposal> SeedModelSwitchProposalAsync(
        IServiceProvider services, IAgent agent, IModelEndpoint proposedEndpoint)
    {
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        return await repo.AddAsync(
            factory(agent, Priority.Medium, "r", proposedEndpoint, null, null, null, null, [], abRun),
            CancellationToken);
    }

    private async Task<IAgentVersion> CreateVersionAsync(
        IServiceProvider services, string prompt, IReadOnlyList<ToolSpecification> tools)
    {
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().GetOrCreateAsync(CancellationToken);
        var template = services.GetRequiredService<IPromptTemplate.Create>()("n", prompt);
        var factory = services.GetRequiredService<IAgentVersion.CreateNew>();
        return factory(agent.CurrentVersion.ProjectId, agent.Id, versionNumber: 999, template, tools);
    }

    private async Task<IOptimizationProposal> WaitForStatusAsync(
        IServiceProvider services, Guid proposalId, ProposalStatus expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        IOptimizationProposal proposal = await Reload(services, proposalId);
        while (proposal.Status != expected && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50, CancellationToken);
            proposal = await Reload(services, proposalId);
        }
        return proposal;
    }

    private async Task<IOptimizationProposal> Reload(IServiceProvider services, Guid id)
        => await services
            .GetRequiredService<IRepository<IOptimizationProposal>>()
            .GetAsync(id, CancellationToken);
}
