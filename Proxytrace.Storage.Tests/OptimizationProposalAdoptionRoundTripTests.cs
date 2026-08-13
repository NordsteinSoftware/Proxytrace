using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class OptimizationProposalAdoptionRoundTripTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AdoptedProposal_RoundTripsAdoptionFields()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        var proposal = await SeedDraftAsync(services);
        var version = await services
            .GetRequiredService<IDomainEntityGenerator<IAgentVersion>>()
            .CreateAsync(CancellationToken);

        var accepted = await proposal.Accept(CancellationToken);
        var adopted = await accepted.MarkAdopted(version, manual: false, CancellationToken);

        var loaded = await repo.GetAsync(proposal.Id, CancellationToken);
        loaded.Status.Should().Be(ProposalStatus.Adopted);
        loaded.AdoptedAt.Should().Be(adopted.AdoptedAt);
        loaded.AdoptedAgentVersionId.Should().Be(version.Id);
        loaded.AdoptedAgentVersionNumber.Should().Be(version.VersionNumber);
        loaded.AdoptedManually.Should().BeFalse();
    }

    [TestMethod]
    public async Task DraftProposal_AdoptionFieldsRoundTripAsNull()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        var proposal = await SeedDraftAsync(services);

        var loaded = await repo.GetAsync(proposal.Id, CancellationToken);

        loaded.AdoptedAt.Should().BeNull();
        loaded.AdoptedAgentVersionId.Should().BeNull();
        loaded.AdoptedAgentVersionNumber.Should().BeNull();
        loaded.AdoptedManually.Should().BeNull();
    }

    [TestMethod]
    public async Task GetByAgentAndStatusAsync_FiltersByAgentAndStatus()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var draft = await SeedDraftAsync(services);
        var other = await SeedDraftAsync(services);
        await other.Accept(CancellationToken);

        var accepted = await repo.GetByAgentAndStatusAsync(
            draft.Agent.Id, ProposalStatus.Accepted, CancellationToken);
        var drafts = await repo.GetByAgentAndStatusAsync(
            draft.Agent.Id, ProposalStatus.Draft, CancellationToken);

        accepted.Should().ContainSingle(p => p.Id == other.Id);
        drafts.Should().ContainSingle(p => p.Id == draft.Id);
        var unrelated = await repo.GetByAgentAndStatusAsync(
            Guid.NewGuid(), ProposalStatus.Draft, CancellationToken);
        unrelated.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetByStatusAsync_ReturnsAllProposalsInStatus()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var first = await SeedDraftAsync(services);
        var second = await SeedDraftAsync(services);
        await first.Accept(CancellationToken);
        await second.Accept(CancellationToken);

        var accepted = await repo.GetByStatusAsync(ProposalStatus.Accepted, CancellationToken);
        var adopted = await repo.GetByStatusAsync(ProposalStatus.Adopted, CancellationToken);

        accepted.Select(p => p.Id).Should().BeEquivalentTo([first.Id, second.Id]);
        adopted.Should().BeEmpty();
    }

    private async Task<IOptimizationProposal> SeedDraftAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().GetOrCreateAsync();
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        var proposal = factory(agent, Priority.Medium, "r", $"proposed prompt {Guid.NewGuid()}", null, null, [], abRun);
        return await repo.AddAsync(proposal, CancellationToken);
    }
}
