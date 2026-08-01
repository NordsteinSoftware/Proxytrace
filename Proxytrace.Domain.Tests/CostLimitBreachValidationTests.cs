using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class CostLimitBreachValidationTests : DomainTest<Module>
{
    private static DateTimeOffset ThisMonth
        => CostMonth.StartOf(DateTimeOffset.UtcNow);

    private static async Task<ICostLimit> CreateLimit(
        IServiceProvider services, IProject project, IAgent? agent, CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var repository = services.GetRequiredService<ICostLimitRepository>();
        return await repository.AddAsync(factory(project, agent, null, 50m, 100m, true), cancellationToken);
    }

    [TestMethod]
    public async Task CreateNew_WithMonthStart_CreatesBreach()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        ICostLimitBreach breach = factory(limit, ThisMonth, CostThreshold.Hard, 123.45m);

        breach.CostLimit.Should().Be(limit);
        breach.MonthStart.Should().Be(ThisMonth);
        breach.Threshold.Should().Be(CostThreshold.Hard);
        breach.SpendEur.Should().Be(123.45m);
        breach.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task CreateNew_WithMidMonthTimestamp_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        // A mid-month key would split one month into two buckets and let an alert fire twice.
        var act = () => factory(limit, ThisMonth.AddDays(3), CostThreshold.Soft, 1m);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithNegativeSpend_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        var act = () => factory(limit, ThisMonth, CostThreshold.Soft, -1m);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task GetFiredThresholdsAsync_ReturnsOnlyThatMonthsBreaches()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        await breaches.AddAsync(factory(limit, ThisMonth, CostThreshold.Soft, 60m), CancellationToken);
        await breaches.AddAsync(factory(limit, ThisMonth.AddMonths(-1), CostThreshold.Hard, 200m), CancellationToken);

        IReadOnlyList<FiredThreshold> current =
            await breaches.GetFiredThresholdsAsync(ThisMonth, cancellationToken: CancellationToken);

        FiredThreshold fired = current.Should().ContainSingle().Subject;
        fired.Threshold.Should().Be(CostThreshold.Soft);
        fired.CostLimitId.Should().Be(limit.Id);
    }

    [TestMethod]
    public async Task GetFiredThresholdsAsync_WithoutProject_ReturnsEveryProjectsBreaches()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        IProject one = await projectGenerator.CreateAsync(CancellationToken);
        IProject two = await projectGenerator.CreateAsync(CancellationToken);
        ICostLimit limitOne = await CreateLimit(services, one, null, CancellationToken);
        ICostLimit limitTwo = await CreateLimit(services, two, null, CancellationToken);

        await breaches.AddAsync(factory(limitOne, ThisMonth, CostThreshold.Soft, 60m), CancellationToken);
        await breaches.AddAsync(factory(limitTwo, ThisMonth, CostThreshold.Hard, 200m), CancellationToken);

        // The guard evaluates every tenant in one tick, so its read is deliberately unscoped.
        IReadOnlyList<FiredThreshold> all =
            await breaches.GetFiredThresholdsAsync(ThisMonth, cancellationToken: CancellationToken);

        all.Select(f => f.CostLimitId).Should().BeEquivalentTo([limitOne.Id, limitTwo.Id]);
    }

    [TestMethod]
    public async Task GetFiredThresholdsAsync_WithProject_ExcludesOtherTenantsBreaches()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();

        IProject mine = await projectGenerator.CreateAsync(CancellationToken);
        IProject theirs = await projectGenerator.CreateAsync(CancellationToken);
        ICostLimit myLimit = await CreateLimit(services, mine, null, CancellationToken);
        ICostLimit theirLimit = await CreateLimit(services, theirs, null, CancellationToken);

        await breaches.AddAsync(factory(myLimit, ThisMonth, CostThreshold.Soft, 60m), CancellationToken);
        await breaches.AddAsync(factory(theirLimit, ThisMonth, CostThreshold.Hard, 200m), CancellationToken);

        // The Costs page reads one project; another tenant's threshold crossings are neither its
        // business nor its cost to pay for.
        IReadOnlyList<FiredThreshold> scoped =
            await breaches.GetFiredThresholdsAsync(ThisMonth, mine.Id, CancellationToken);

        scoped.Should().ContainSingle().Which.CostLimitId.Should().Be(myLimit.Id);
    }

    [TestMethod]
    public async Task DeleteForLimitAsync_RemovesEveryBreachOfThatLimit()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        await breaches.AddAsync(factory(limit, ThisMonth, CostThreshold.Soft, 60m), CancellationToken);
        await breaches.AddAsync(factory(limit, ThisMonth, CostThreshold.Hard, 120m), CancellationToken);

        await breaches.DeleteForLimitAsync(limit.Id, CancellationToken);

        IReadOnlyList<FiredThreshold> remaining =
            await breaches.GetFiredThresholdsAsync(ThisMonth, cancellationToken: CancellationToken);
        remaining.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetActiveHardBlocksAsync_ReturnsProjectScopedBlockWithoutAgent()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        await breaches.AddAsync(factory(limit, ThisMonth, CostThreshold.Hard, 150m), CancellationToken);

        IReadOnlyList<BudgetHardBlock> blocks =
            await breaches.GetActiveHardBlocksAsync(project.Id, ThisMonth, CancellationToken);

        BudgetHardBlock block = blocks.Should().ContainSingle().Subject;
        block.CostLimitId.Should().Be(limit.Id);
        block.AgentId.Should().BeNull();
        block.AgentName.Should().BeNull();
    }

    [TestMethod]
    public async Task GetActiveHardBlocksAsync_JoinsTheScopedAgentName()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IAgent agent = await GetOrCreate<IAgent>(services);
        ICostLimit limit = await CreateLimit(services, agent.Project, agent, CancellationToken);

        await breaches.AddAsync(factory(limit, ThisMonth, CostThreshold.Hard, 150m), CancellationToken);

        IReadOnlyList<BudgetHardBlock> blocks =
            await breaches.GetActiveHardBlocksAsync(agent.Project.Id, ThisMonth, CancellationToken);

        BudgetHardBlock block = blocks.Should().ContainSingle().Subject;
        block.AgentId.Should().Be(agent.Id);
        block.AgentName.Should().Be(agent.Name);
    }

    [TestMethod]
    public async Task GetActiveHardBlocksAsync_IgnoresSoftBreaches()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        await breaches.AddAsync(factory(limit, ThisMonth, CostThreshold.Soft, 60m), CancellationToken);

        IReadOnlyList<BudgetHardBlock> blocks =
            await breaches.GetActiveHardBlocksAsync(project.Id, ThisMonth, CancellationToken);

        blocks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetActiveHardBlocksAsync_IgnoresBreachesOfDisabledLimits()
    {
        IServiceProvider services = GetServices();
        var createLimit = services.GetRequiredService<ICostLimit.CreateNew>();
        var limits = services.GetRequiredService<ICostLimitRepository>();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IProject project = await GetOrCreate<IProject>(services);

        ICostLimit limit = await limits.AddAsync(createLimit(project, null, null, null, 100m, true), CancellationToken);
        await breaches.AddAsync(factory(limit, ThisMonth, CostThreshold.Hard, 150m), CancellationToken);

        // Disabling the limit must lift the block immediately, without deleting the breach record.
        await limit.Update(null, 100m, enabled: false, CancellationToken);

        IReadOnlyList<BudgetHardBlock> blocks =
            await breaches.GetActiveHardBlocksAsync(project.Id, ThisMonth, CancellationToken);

        blocks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetActiveHardBlocksAsync_IgnoresOtherMonths()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ICostLimitBreach.CreateNew>();
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IProject project = await GetOrCreate<IProject>(services);
        ICostLimit limit = await CreateLimit(services, project, null, CancellationToken);

        await breaches.AddAsync(factory(limit, ThisMonth.AddMonths(-1), CostThreshold.Hard, 150m), CancellationToken);

        // The month rollover is what lifts a block; nothing is cleaned up on the 1st.
        IReadOnlyList<BudgetHardBlock> blocks =
            await breaches.GetActiveHardBlocksAsync(project.Id, ThisMonth, CancellationToken);

        blocks.Should().BeEmpty();
    }
}
