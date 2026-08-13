using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AgentArchiveTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ArchiveAsync_ExcludesAgentFromGetByProject()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IAgentRepository>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(agent.Id, CancellationToken);

        var results = await repository.GetByProjectAsync(agent.Project.Id, CancellationToken);
        results.Should().NotContain(a => a.Id == agent.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_ExcludesAgentFromGetAll()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IAgentRepository>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(agent.Id, CancellationToken);

        var all = await repository.GetAllAsync(CancellationToken);
        all.Should().NotContain(a => a.Id == agent.Id);
    }

    [TestMethod]
    public async Task ArchiveAsync_KeepsAgentResolvableById()
    {
        // Agentic evaluators and captured calls live-fetch the agent by id, so it must still resolve.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IAgentRepository>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        await repository.ArchiveAsync(agent.Id, CancellationToken);

        var retrieved = await repository.GetAsync(agent.Id, CancellationToken);
        retrieved.Id.Should().Be(agent.Id);
        retrieved.IsArchived.Should().BeTrue();
    }

    [TestMethod]
    public async Task ArchiveAsync_ExcludesAgentFromLicenseCount()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IAgentRepository>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var before = await repository.CountNonSystemAsync(CancellationToken);
        await repository.ArchiveAsync(agent.Id, CancellationToken);
        var after = await repository.CountNonSystemAsync(CancellationToken);

        after.Should().Be(before - 1);
    }

    [TestMethod]
    public async Task ArchiveAsync_MissingAgent_ReturnsFalse()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IAgentRepository>();

        var archived = await repository.ArchiveAsync(Guid.NewGuid(), CancellationToken);

        archived.Should().BeFalse();
    }
}
