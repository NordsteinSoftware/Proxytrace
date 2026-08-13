using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// The project-scoped lookups behind an unfiltered proposals list (#482): a caller who may read
/// several projects is answered with the union of exactly those, resolved in the query.
/// </summary>
[TestClass]
public sealed class OptimizationProposalRepositoryScopeTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetByProjects_ReturnsOnlyProposalsOfTheGivenProjects()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var first = await PersistProposalInNewProject(services);
        var second = await PersistProposalInNewProject(services);
        var outsider = await PersistProposalInNewProject(services);

        var proposals = await repo.GetByProjectsAsync([first.ProjectId, second.ProjectId], CancellationToken);

        proposals.Select(p => p.Id).Should().BeEquivalentTo([first.Proposal.Id, second.Proposal.Id]);
        proposals.Select(p => p.Id).Should().NotContain(outsider.Proposal.Id);
    }

    [TestMethod]
    public async Task GetByProjects_WithNoProjects_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        await PersistProposalInNewProject(services);

        var proposals = await repo.GetByProjectsAsync([], CancellationToken);

        proposals.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetByProject_StillReturnsOnlyThatProject()
    {
        // GetByProjectAsync now delegates to the set overload — the single-project contract holds.
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var mine = await PersistProposalInNewProject(services);
        await PersistProposalInNewProject(services);

        var proposals = await repo.GetByProjectAsync(mine.ProjectId, CancellationToken);

        proposals.Should().ContainSingle().Which.Id.Should().Be(mine.Proposal.Id);
    }

    // A proposal whose agent lives in a freshly created project, so each call yields a distinct
    // project — the generators reuse one project and cannot express this.
    private async Task<(Guid ProjectId, IOptimizationProposal Proposal)> PersistProposalInNewProject(
        IServiceProvider services)
    {
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>()
            .GetOrCreateAsync(CancellationToken);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .CreateAsync(CancellationToken);

        var agent = await services.GetRequiredService<IAgentRepository>().CreateWithInitialVersionAsync(
            name: $"A-{Guid.NewGuid():N}",
            systemPrompt: services.GetRequiredService<IPromptTemplate.Create>()("T", "You are a test agent."),
            tools: [],
            project: project,
            endpoint: endpoint,
            modelParameters: services.GetRequiredService<IModelParameters.Create>()(null, null, null, null, null),
            isSystemAgent: false,
            cancellationToken: CancellationToken);

        var abRun = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>()
            .CreateAsync(CancellationToken);
        var proposal = await services.GetRequiredService<IOptimizationProposalRepository>().AddAsync(
            services.GetRequiredService<ISystemPromptProposal.CreateNew>()(
                agent, Priority.Medium, "r", "proposed", null, null, [], abRun),
            CancellationToken);

        return (project.Id, proposal);
    }
}
