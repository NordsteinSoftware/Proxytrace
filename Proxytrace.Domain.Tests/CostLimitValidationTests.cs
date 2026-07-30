using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class CostLimitValidationTests : DomainTest<Module>
{
    // ── factory / construction ────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateNew_WithBothThresholds_CreatesProjectScopedLimit()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);

        ICostLimit limit = factory(project, null, null, 50m, 100m, true);

        limit.Should().NotBeNull();
        limit.Project.Should().Be(project);
        limit.Agent.Should().BeNull();
        limit.SoftLimitEur.Should().Be(50m);
        limit.HardLimitEur.Should().Be(100m);
        limit.Enabled.Should().BeTrue();
        limit.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task CreateNew_WithAgent_CreatesAgentScopedLimit()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IAgent agent = await GetOrCreate<IAgent>(services);

        ICostLimit limit = factory(agent.Project, agent, null, null, 25m, true);

        limit.Agent.Should().Be(agent);
        limit.SoftLimitEur.Should().BeNull();
        limit.HardLimitEur.Should().Be(25m);
    }

    [TestMethod]
    public async Task CreateNew_WithApiKey_CreatesKeyScopedLimit()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IApiKey apiKey = await GetOrCreate<IApiKey>(services);

        ICostLimit limit = factory(apiKey.Project, null, apiKey, 20m, 40m, true);

        limit.ApiKey.Should().Be(apiKey);
        limit.Agent.Should().BeNull();
        limit.HardLimitEur.Should().Be(40m);
    }

    [TestMethod]
    public async Task CreateNew_WithBothAgentAndApiKey_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IAgent agent = await GetOrCreate<IAgent>(services);
        var keyGenerator = services.GetRequiredService<IDomainEntityGenerator<IApiKey>>();
        IApiKey apiKey = await keyGenerator.CreateAsync(CancellationToken);

        // A budget has exactly one scope: the partial unique indexes assume it, and "agent X via
        // key Y" is a cross-product the proxy's scope matching does not model.
        var act = () => factory(agent.Project, agent, apiKey, null, 10m, true);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithApiKeyFromAnotherProject_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        IApiKey apiKey = await GetOrCreate<IApiKey>(services);
        IProject otherProject = await projectGenerator.CreateAsync(CancellationToken);

        // Otherwise the budget would measure spend the project never incurred.
        var act = () => factory(otherProject, null, apiKey, null, 10m, true);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_CalledTwice_ProducesDifferentIds()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);

        ICostLimit first = factory(project, null, null, 10m, 20m, true);
        ICostLimit second = factory(project, null, null, 10m, 20m, true);

        first.Id.Should().NotBe(second.Id);
    }

    [TestMethod]
    public async Task CreateExisting_RoundTripsEveryProperty()
    {
        IServiceProvider services = GetServices();
        var createNew = services.GetRequiredService<ICostLimit.CreateNew>();
        var createExisting = services.GetRequiredService<ICostLimit.CreateExisting>();
        IAgent agent = await GetOrCreate<IAgent>(services);

        ICostLimit original = createNew(agent.Project, agent, null, 5m, 10m, true);
        ICostLimit restored = createExisting(agent.Project, agent, null, 5m, 10m, true, original);

        restored.Id.Should().Be(original.Id);
        restored.CreatedAt.Should().Be(original.CreatedAt);
        restored.UpdatedAt.Should().Be(original.UpdatedAt);
        restored.Agent.Should().Be(agent);
        restored.SoftLimitEur.Should().Be(5m);
        restored.HardLimitEur.Should().Be(10m);
    }

    // ── validation (enforced on activation, so an invalid limit never materializes) ──

    [TestMethod]
    public async Task CreateNew_WithNeitherThresholdSet_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);

        var act = () => factory(project, null, null, null, null, true);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithNonPositiveSoftLimit_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);

        var act = () => factory(project, null, null, 0m, 100m, true);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithNegativeHardLimit_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);

        var act = () => factory(project, null, null, null, -1m, true);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithSoftAboveHard_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);

        // A soft threshold above the hard one could never fire — the hard limit blocks first.
        var act = () => factory(project, null, null, 200m, 100m, true);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithSoftEqualToHard_IsAccepted()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);

        ICostLimit limit = factory(project, null, null, 100m, 100m, true);

        limit.SoftLimitEur.Should().Be(limit.HardLimitEur);
    }

    [TestMethod]
    public async Task CreateNew_WithAgentFromAnotherProject_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        IAgent agent = await GetOrCreate<IAgent>(services);
        IProject otherProject = await projectGenerator.CreateAsync(CancellationToken);

        var act = () => factory(otherProject, agent, null, null, 10m, true);

        act.Should().Throw<Exception>();
    }

    // ── mutation ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Update_WithNewThresholds_PersistsThem()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var repository = services.GetRequiredService<ICostLimitRepository>();
        IProject project = await GetOrCreate<IProject>(services);

        ICostLimit saved = await repository.AddAsync(factory(project, null, null, 50m, 100m, true), CancellationToken);
        await saved.Update(75m, 150m, false, CancellationToken);

        ICostLimit reloaded = await repository.GetAsync(saved.Id, CancellationToken);
        reloaded.SoftLimitEur.Should().Be(75m);
        reloaded.HardLimitEur.Should().Be(150m);
        reloaded.Enabled.Should().BeFalse();
    }

    [TestMethod]
    public async Task Update_WithSoftAboveHard_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var repository = services.GetRequiredService<ICostLimitRepository>();
        IProject project = await GetOrCreate<IProject>(services);

        ICostLimit saved = await repository.AddAsync(factory(project, null, null, 50m, 100m, true), CancellationToken);

        await FluentActions
            .Invoking(() => saved.Update(200m, 100m, true, CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    // ── repository queries ────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAllEnabledAsync_ReturnsOnlyEnabledLimits()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var repository = services.GetRequiredService<ICostLimitRepository>();
        IAgent agent = await GetOrCreate<IAgent>(services);

        await repository.AddAsync(factory(agent.Project, null, null, 50m, 100m, true), CancellationToken);
        await repository.AddAsync(factory(agent.Project, agent, null, null, 10m, false), CancellationToken);

        IReadOnlyList<ICostLimit> enabled = await repository.GetAllEnabledAsync(CancellationToken);

        enabled.Should().ContainSingle().Which.Agent.Should().BeNull();
    }

    [TestMethod]
    public async Task GetByProjectAsync_ReturnsProjectAndAgentScopedLimits()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var repository = services.GetRequiredService<ICostLimitRepository>();
        IAgent agent = await GetOrCreate<IAgent>(services);

        await repository.AddAsync(factory(agent.Project, null, null, 50m, 100m, true), CancellationToken);
        await repository.AddAsync(factory(agent.Project, agent, null, 5m, 10m, true), CancellationToken);

        IReadOnlyList<ICostLimit> limits = await repository.GetByProjectAsync(agent.Project.Id, CancellationToken);

        limits.Should().HaveCount(2);
        limits.Should().ContainSingle(l => l.Agent != null && l.Agent.Id == agent.Id);
    }
}
