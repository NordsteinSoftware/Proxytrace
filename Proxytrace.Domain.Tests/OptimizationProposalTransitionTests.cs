using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.OptimizationProposal;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class OptimizationProposalTransitionTests : DomainTest<Module>
{
    [TestMethod]
    public async Task Accept_FromDraft_TransitionsToAcceptedAndPersists()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);

        var accepted = await proposal.Accept(CancellationToken);

        accepted.Status.Should().Be(ProposalStatus.Accepted);
        var reloaded = await Reload(services, accepted.Id);
        reloaded.Status.Should().Be(ProposalStatus.Accepted);
    }

    [TestMethod]
    public async Task Accept_WhenAlreadyAccepted_Throws()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);
        var accepted = await proposal.Accept(CancellationToken);

        await FluentActions
            .Invoking(() => accepted.Accept(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Reject_FromDraft_TransitionsToRejected()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);

        var rejected = await proposal.Reject(CancellationToken);

        rejected.Status.Should().Be(ProposalStatus.Rejected);
        rejected.AdoptedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task Reject_FromAccepted_Throws()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);
        var accepted = await proposal.Accept(CancellationToken);

        await FluentActions
            .Invoking(() => accepted.Reject(CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task MarkAdopted_FromAccepted_Manually_SetsAdoptionFieldsAndPersists()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);
        var accepted = await proposal.Accept(CancellationToken);

        var adopted = await accepted.MarkAdopted(null, manual: true, CancellationToken);

        adopted.Status.Should().Be(ProposalStatus.Adopted);
        adopted.AdoptedManually.Should().BeTrue();
        adopted.AdoptedAgentVersionId.Should().BeNull();
        adopted.AdoptedAgentVersionNumber.Should().BeNull();
        adopted.AdoptedAt.Should().NotBeNull();
        adopted.AdoptedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        var reloaded = await Reload(services, adopted.Id);
        reloaded.Status.Should().Be(ProposalStatus.Adopted);
        reloaded.AdoptedManually.Should().BeTrue();
    }

    [TestMethod]
    public async Task MarkAdopted_FromAccepted_WithVersion_LinksVersionIdAndNumber()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);
        var accepted = await proposal.Accept(CancellationToken);
        var version = await services
            .GetRequiredService<IDomainEntityGenerator<IAgentVersion>>()
            .CreateAsync(CancellationToken);

        var adopted = await accepted.MarkAdopted(version, manual: false, CancellationToken);

        adopted.AdoptedManually.Should().BeFalse();
        adopted.AdoptedAgentVersionId.Should().Be(version.Id);
        adopted.AdoptedAgentVersionNumber.Should().Be(version.VersionNumber);
    }

    [TestMethod]
    public async Task MarkAdopted_FromDraft_Throws()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);

        await FluentActions
            .Invoking(() => proposal.MarkAdopted(null, manual: true, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task MarkAdopted_WhenAlreadyAdopted_Throws()
    {
        IServiceProvider services = GetServices();
        var proposal = await CreateDraftAsync(services);
        var accepted = await proposal.Accept(CancellationToken);
        var adopted = await accepted.MarkAdopted(null, manual: true, CancellationToken);

        await FluentActions
            .Invoking(() => adopted.MarkAdopted(null, manual: true, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task CreateExisting_AdoptedWithoutAdoptedAt_Throws()
    {
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ISystemPromptProposal.CreateExisting>();
        var source = await services
            .GetRequiredService<IDomainEntityGenerator<ISystemPromptProposal>>()
            .CreateAsync(CancellationToken);

        var action = () => createExisting(
            source.Agent, ProposalStatus.Adopted, source.Priority, source.Rationale,
            source.ProposedSystemMessage, source.CurrentPassRate, source.ProposedPassRate,
            source.EvidenceTestRunIds, source.ABTestRun, source.ContentHash,
            adoptedAt: null, adoptedAgentVersionId: null, adoptedAgentVersionNumber: null, adoptedManually: null,
            existing: source);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateExisting_DraftWithAdoptionFields_Throws()
    {
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ISystemPromptProposal.CreateExisting>();
        var source = await services
            .GetRequiredService<IDomainEntityGenerator<ISystemPromptProposal>>()
            .CreateAsync(CancellationToken);

        var action = () => createExisting(
            source.Agent, ProposalStatus.Draft, source.Priority, source.Rationale,
            source.ProposedSystemMessage, source.CurrentPassRate, source.ProposedPassRate,
            source.EvidenceTestRunIds, source.ABTestRun, source.ContentHash,
            adoptedAt: DateTimeOffset.UtcNow.AddMinutes(-1), adoptedAgentVersionId: null,
            adoptedAgentVersionNumber: null, adoptedManually: true,
            existing: source);

        action.Should().Throw<Exception>();
    }

    private async Task<IOptimizationProposal> CreateDraftAsync(IServiceProvider services)
        => await services
            .GetRequiredService<IDomainEntityGenerator<ISystemPromptProposal>>()
            .CreateAsync(CancellationToken);

    private async Task<IOptimizationProposal> Reload(IServiceProvider services, Guid id)
        => await services
            .GetRequiredService<IRepository<IOptimizationProposal>>()
            .GetAsync(id, CancellationToken);
}
