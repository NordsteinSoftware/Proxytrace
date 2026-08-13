using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.CustomAnomaly;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class CustomAnomalyDetectorRepositoryTests : BaseTest<Module>
{
    private static Task<IAgent> CreateSystemAgent(IServiceProvider services, string name, CancellationToken cancellationToken)
        => services.GetRequiredService<IAgentGenerator>()
            .CreateAsync(name, isSystemAgent: true, cancellationToken: cancellationToken);

    [TestMethod]
    public async Task AddAsync_WithMixedTriggers_RoundTripsTriggerJson()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);
        var triggers = new[]
        {
            new AnomalyTrigger(TriggerKind.Phrase, "refund"),
            new AnomalyTrigger(TriggerKind.Regex, @"escalat(e|ion)"),
        };

        var detector = await repo.AddAsync(
            factory("Refunds", judge, triggers, true, [], true, false), CancellationToken);

        var reloaded = await repo.GetAsync(detector.Id, CancellationToken);
        reloaded.Triggers.Should().BeEquivalentTo(triggers, o => o.WithStrictOrdering());
        reloaded.Agent.Id.Should().Be(judge.Id);
        reloaded.AllAgents.Should().BeTrue();
        reloaded.ScopedAgents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateRelationsAsync_WhenScopeChanges_SyncsJunctionOnReload()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);
        var agentA = await agentGenerator.CreateAsync("Agent A", cancellationToken: CancellationToken);
        var agentB = await agentGenerator.CreateAsync("Agent B", cancellationToken: CancellationToken);

        var detector = await repo.AddAsync(
            factory("Scoped", judge, [new AnomalyTrigger(TriggerKind.Phrase, "refund")], false, [agentA], true, false),
            CancellationToken);

        // Swap the single scoped agent for a different one.
        var updated = await detector.Update(
            detector.Name, detector.Triggers, false, [agentB], detector.IsEnabled, detector.BlockUpstream, CancellationToken);

        var reloaded = await repo.GetAsync(updated.Id, CancellationToken);
        reloaded.ScopedAgents.Should().ContainSingle().Which.Id.Should().Be(agentB.Id);
    }

    [TestMethod]
    public async Task UpdateRelationsAsync_WhenSwitchedToAllAgents_ClearsJunction()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);
        var agent = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("Agent", cancellationToken: CancellationToken);

        var detector = await repo.AddAsync(
            factory("Scoped", judge, [new AnomalyTrigger(TriggerKind.Phrase, "refund")], false, [agent], true, false),
            CancellationToken);

        var updated = await detector.Update(
            detector.Name, detector.Triggers, true, [], detector.IsEnabled, detector.BlockUpstream, CancellationToken);

        var reloaded = await repo.GetAsync(updated.Id, CancellationToken);
        reloaded.AllAgents.Should().BeTrue();
        reloaded.ScopedAgents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetEnabledByProjectAsync_ReturnsOnlyEnabledDetectorsOfProject()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);
        var trigger = new AnomalyTrigger(TriggerKind.Phrase, "refund");

        var enabled = await repo.AddAsync(factory("Enabled", judge, [trigger], true, [], true, false), CancellationToken);
        await repo.AddAsync(factory("Disabled", judge, [trigger], true, [], false, false), CancellationToken);

        var result = await repo.GetEnabledByProjectAsync(judge.Project.Id, CancellationToken);

        result.Should().ContainSingle().Which.Id.Should().Be(enabled.Id);

        var otherProject = await repo.GetEnabledByProjectAsync(Guid.NewGuid(), CancellationToken);
        otherProject.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetByProjectAsync_ReturnsEnabledAndDisabledDetectors()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);
        var trigger = new AnomalyTrigger(TriggerKind.Phrase, "refund");

        await repo.AddAsync(factory("Enabled", judge, [trigger], true, [], true, false), CancellationToken);
        await repo.AddAsync(factory("Disabled", judge, [trigger], true, [], false, false), CancellationToken);

        var result = await repo.GetByProjectAsync(judge.Project.Id, CancellationToken);

        result.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GetEnabledBlockingRulesByProjectAsync_ReturnsOnlyEnabledBlockingDetectors()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);
        var trigger = new AnomalyTrigger(TriggerKind.Phrase, "hunter2");

        var blocking = await repo.AddAsync(
            factory("Blocking", judge, [trigger], true, [], true, true), CancellationToken);
        await repo.AddAsync(factory("Non-blocking", judge, [trigger], true, [], true, false), CancellationToken);
        await repo.AddAsync(factory("Disabled blocking", judge, [trigger], true, [], false, true), CancellationToken);

        var rules = await repo.GetEnabledBlockingRulesByProjectAsync(judge.Project.Id, CancellationToken);

        var rule = rules.Should().ContainSingle().Which;
        rule.DetectorId.Should().Be(blocking.Id);
        rule.DetectorName.Should().Be("Blocking");
        rule.AllAgents.Should().BeTrue();
        rule.ScopedAgentNames.Should().BeEmpty();
        rule.Triggers.Should().ContainSingle().Which.Should().Be(trigger);

        (await repo.GetEnabledBlockingRulesByProjectAsync(Guid.NewGuid(), CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetEnabledBlockingRulesByProjectAsync_ProjectsScopedAgentNames()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);
        var scoped = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("Billing Agent", cancellationToken: CancellationToken);

        await repo.AddAsync(
            factory("Scoped blocking", judge, [new AnomalyTrigger(TriggerKind.Phrase, "hunter2")],
                false, [scoped], true, true),
            CancellationToken);

        var rules = await repo.GetEnabledBlockingRulesByProjectAsync(judge.Project.Id, CancellationToken);

        var rule = rules.Should().ContainSingle().Which;
        rule.AllAgents.Should().BeFalse();
        rule.ScopedAgentNames.Should().ContainSingle().Which.Should().Be("Billing Agent");
    }

    [TestMethod]
    public async Task AddAsync_BlockUpstream_RoundTrips()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyDetectorRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyDetector.CreateNew>();
        var judge = await CreateSystemAgent(services, "Judge", CancellationToken);

        var detector = await repo.AddAsync(
            factory("Blocking", judge, [new AnomalyTrigger(TriggerKind.Phrase, "hunter2")], true, [], true, true),
            CancellationToken);

        var reloaded = await repo.GetAsync(detector.Id, CancellationToken);
        reloaded.BlockUpstream.Should().BeTrue();

        var updated = await reloaded.Update(
            reloaded.Name, reloaded.Triggers, reloaded.AllAgents, [], reloaded.IsEnabled,
            blockUpstream: false, CancellationToken);
        (await repo.GetAsync(updated.Id, CancellationToken)).BlockUpstream.Should().BeFalse();
    }
}
