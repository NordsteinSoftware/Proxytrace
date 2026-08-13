using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class OptimizationProposalPassRateRoundTripTests : BaseTest<Module>
{
    [TestMethod]
    public async Task SystemPromptProposal_RoundTripsPassRates()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Medium, "r", "msg", 0.42, 0.78, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var loaded = await repo.GetAsync(proposal.Id, CancellationToken);

        loaded.Should().BeAssignableTo<ISystemPromptProposal>();
        loaded.CurrentPassRate.Should().Be(0.42);
        loaded.ProposedPassRate.Should().Be(0.78);
        loaded.ExpectedPassRateDelta.Should().BeApproximately(0.36, 1e-9);
    }

    [TestMethod]
    public async Task ToolUpdateProposal_RoundTripsPassRates()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.High, "r", [], 0.21, 0.63, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var loaded = await repo.GetAsync(proposal.Id, CancellationToken);

        loaded.Should().BeAssignableTo<IToolUpdateProposal>();
        loaded.CurrentPassRate.Should().Be(0.21);
        loaded.ProposedPassRate.Should().Be(0.63);
    }

    [TestMethod]
    public async Task ModelSwitchProposal_RoundTripsPassRates()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Critical, "r", endpoint, 0.18, 0.92, -0.001m, TimeSpan.FromMilliseconds(-50), [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var loaded = await repo.GetAsync(proposal.Id, CancellationToken);

        loaded.Should().BeAssignableTo<IModelSwitchProposal>();
        loaded.CurrentPassRate.Should().Be(0.18);
        loaded.ProposedPassRate.Should().Be(0.92);
        loaded.ExpectedPassRateDelta.Should().BeApproximately(0.74, 1e-9);
    }

    [TestMethod]
    public async Task RoundTrip_NullPassRates_PreservedAsNull()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<IOptimizationProposal>>();
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var agent = await CreateAgentAsync(services);
        var abRun = await CreateTestRunAsync(services);

        var proposal = factory(agent, Priority.Low, "r", "msg", null, null, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var loaded = await repo.GetAsync(proposal.Id, CancellationToken);

        loaded.CurrentPassRate.Should().BeNull();
        loaded.ProposedPassRate.Should().BeNull();
        loaded.ExpectedPassRateDelta.Should().BeNull();
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
