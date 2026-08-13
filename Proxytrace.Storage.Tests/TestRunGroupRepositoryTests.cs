using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class TestRunGroupRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AddAsync_RoundTripsSampleCount()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetSuite(services);

        var saved = await repo.AddAsync(factory(suite, isSystemRun: false, null, sampleCount: 4), CancellationToken);
        var reloaded = await repo.GetAsync(saved.Id, CancellationToken);

        reloaded.SampleCount.Should().Be(4);
    }

    [TestMethod]
    public async Task GetByProjectPaged_ByDefault_ExcludesSystemRuns()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetSuite(services);

        await repo.AddAsync(factory(suite, isSystemRun: false, null, sampleCount: 1), CancellationToken);
        await repo.AddAsync(factory(suite, isSystemRun: true, null, sampleCount: 1), CancellationToken);

        var page = await repo.GetByProjectPagedAsync(suite.Agent.Project.Id, page: 1, pageSize: 50, cancellationToken: CancellationToken);

        page.Items.Should().ContainSingle();
        page.Items.Single().IsSystemRun.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetByProjectPaged_WithIncludeSystem_ReturnsSystemRuns()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetSuite(services);

        await repo.AddAsync(factory(suite, isSystemRun: false, null, sampleCount: 1), CancellationToken);
        await repo.AddAsync(factory(suite, isSystemRun: true, null, sampleCount: 1), CancellationToken);

        var page = await repo.GetByProjectPagedAsync(suite.Agent.Project.Id, page: 1, pageSize: 50, includeSystem: true, CancellationToken);

        page.Items.Should().HaveCount(2);
        page.Items.Should().Contain(g => g.IsSystemRun);
    }

    [TestMethod]
    public async Task GetByAgentPaged_WithIncludeSystem_ReturnsSystemRuns()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetSuite(services);

        await repo.AddAsync(factory(suite, isSystemRun: true, null, sampleCount: 1), CancellationToken);

        var excluded = await repo.GetByAgentPagedAsync(suite.Agent.Id, page: 1, pageSize: 50, cancellationToken: CancellationToken);
        var included = await repo.GetByAgentPagedAsync(suite.Agent.Id, page: 1, pageSize: 50, includeSystem: true, CancellationToken);

        excluded.Items.Should().BeEmpty();
        included.Items.Should().ContainSingle(g => g.IsSystemRun);
    }

    [TestMethod]
    public async Task GetBySchedule_ReturnsOnlyTaggedGroups_NewestFirst()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetSuite(services);
        var schedule = await services
            .GetRequiredService<IDomainEntityGenerator<ITestRunSchedule>>()
            .CreateAsync(CancellationToken);

        var older = await repo.AddAsync(factory(suite, isSystemRun: false, schedule.Id, sampleCount: 1), CancellationToken);
        var newer = await repo.AddAsync(factory(suite, isSystemRun: false, schedule.Id, sampleCount: 1), CancellationToken);
        // An untagged manual run that must be excluded.
        await repo.AddAsync(factory(suite, isSystemRun: false, null, sampleCount: 1), CancellationToken);

        var result = await repo.GetByScheduleAsync(schedule.Id, take: 5, CancellationToken);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(g => g.ScheduleId == schedule.Id);
        result.Select(g => g.Id).Should().Equal(newer.Id, older.Id);
    }

    [TestMethod]
    public async Task GetBySuitePaged_ReturnsOnlyGroupsForThatSuite()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suiteGen = services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>();
        var suiteA = await suiteGen.CreateAsync(CancellationToken);
        var suiteB = await suiteGen.CreateAsync(CancellationToken);

        await repo.AddAsync(factory(suiteA, isSystemRun: false, null, sampleCount: 1), CancellationToken);
        await repo.AddAsync(factory(suiteB, isSystemRun: false, null, sampleCount: 1), CancellationToken);

        var page = await repo.GetBySuitePagedAsync(suiteA.Id, page: 1, pageSize: 50, cancellationToken: CancellationToken);

        page.Items.Should().ContainSingle();
        page.Items.Single().Suite.Id.Should().Be(suiteA.Id);
        page.Total.Should().Be(1);
    }

    [TestMethod]
    public async Task GetBySuitePaged_ByDefault_ExcludesSystemRuns()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetSuite(services);

        await repo.AddAsync(factory(suite, isSystemRun: false, null, sampleCount: 1), CancellationToken);
        await repo.AddAsync(factory(suite, isSystemRun: true, null, sampleCount: 1), CancellationToken);

        var page = await repo.GetBySuitePagedAsync(suite.Id, page: 1, pageSize: 50, cancellationToken: CancellationToken);

        page.Items.Should().ContainSingle();
        page.Items.Single().IsSystemRun.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetBySuitePaged_OrdersNewestFirst()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetSuite(services);

        var older = await repo.AddAsync(factory(suite, isSystemRun: false, null, sampleCount: 1), CancellationToken);
        var newer = await repo.AddAsync(factory(suite, isSystemRun: false, null, sampleCount: 1), CancellationToken);

        var page = await repo.GetBySuitePagedAsync(suite.Id, page: 1, pageSize: 50, cancellationToken: CancellationToken);

        page.Items.Select(g => g.Id).Should().Equal(newer.Id, older.Id);
    }

    private async Task<ITestSuite> GetSuite(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().GetOrCreateAsync(CancellationToken);
}
