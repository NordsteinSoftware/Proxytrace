using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class OptimizationTheoryRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task SystemPromptTheory_RoundTrips()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationTheoryRepository>();
        var factory = services.GetRequiredService<ISystemPromptTheory.CreateNew>();
        var agent = await GetAgent(services);
        var suite = await GetSuite(services);

        var theory = factory(agent, suite, TheorySource.User, Priority.Medium, "r", "new prompt", []);
        await repo.AddAsync(theory, CancellationToken);

        var loaded = await repo.GetAsync(theory.Id, CancellationToken);

        loaded.Should().BeAssignableTo<ISystemPromptTheory>();
        ((ISystemPromptTheory)loaded).ProposedSystemMessage.Should().Be("new prompt");
        loaded.Source.Should().Be(TheorySource.User);
        loaded.Status.Should().Be(TheoryStatus.Proposed);
    }

    [TestMethod]
    public async Task ModelSwitchTheory_RoundTrips()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationTheoryRepository>();
        var factory = services.GetRequiredService<IModelSwitchTheory.CreateNew>();
        var agent = await GetAgent(services);
        var suite = await GetSuite(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);

        var theory = factory(agent, suite, TheorySource.TraceyAi, Priority.High, "r", endpoint, []);
        await repo.AddAsync(theory, CancellationToken);

        var loaded = await repo.GetAsync(theory.Id, CancellationToken);

        loaded.Should().BeAssignableTo<IModelSwitchTheory>();
        ((IModelSwitchTheory)loaded).ProposedEndpoint.Id.Should().Be(endpoint.Id);
    }

    [TestMethod]
    public async Task FindLatestByContentHash_ReturnsMatch()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationTheoryRepository>();
        var factory = services.GetRequiredService<ISystemPromptTheory.CreateNew>();
        var agent = await GetAgent(services);
        var suite = await GetSuite(services);

        var theory = factory(agent, suite, TheorySource.User, Priority.Medium, "r", "hashed prompt", []);
        await repo.AddAsync(theory, CancellationToken);

        var found = await repo.FindLatestByContentHashAsync(agent.Id, theory.ContentHash, CancellationToken);

        found.Should().NotBeNull();
        found.Id.Should().Be(theory.Id);
    }

    [TestMethod]
    public async Task CountByProjectAndStatus_CountsValidating()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationTheoryRepository>();
        var factory = services.GetRequiredService<ISystemPromptTheory.CreateNew>();
        var agent = await GetAgent(services);
        var suite = await GetSuite(services);

        var proposed = factory(agent, suite, TheorySource.User, Priority.Medium, "r", "p1", []);
        await repo.AddAsync(proposed, CancellationToken);

        var toValidate = factory(agent, suite, TheorySource.User, Priority.Medium, "r", "p2", []);
        var persisted = await repo.AddAsync(toValidate, CancellationToken);
        await persisted.SetValidating(CancellationToken);

        var validatingCount = await repo.CountByProjectAndStatusAsync(
            agent.Project.Id, TheoryStatus.Validating, CancellationToken);

        validatingCount.Should().Be(1);
    }

    [TestMethod]
    public async Task CountActiveByProject_CountsProposedAndValidating()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IOptimizationTheoryRepository>();
        var factory = services.GetRequiredService<ISystemPromptTheory.CreateNew>();
        var agent = await GetAgent(services);
        var suite = await GetSuite(services);

        await repo.AddAsync(factory(agent, suite, TheorySource.User, Priority.Medium, "r", "p1", []), CancellationToken);
        var second = await repo.AddAsync(factory(agent, suite, TheorySource.User, Priority.Medium, "r", "p2", []), CancellationToken);
        await second.SetValidating(CancellationToken);

        var active = await repo.CountActiveByProjectAsync(agent.Project.Id, CancellationToken);

        active.Should().Be(2); // one Proposed + one Validating
    }

    private static async Task<IAgent> GetAgent(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().GetOrCreateAsync();

    private async Task<ITestSuite> GetSuite(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().GetOrCreateAsync(CancellationToken);
}
