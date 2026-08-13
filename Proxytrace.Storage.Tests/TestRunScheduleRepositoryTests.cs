using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class TestRunScheduleRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetDueAsync_WhenEnabledAndNextRunInPast_ReturnsSchedule()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        var due = await PersistSchedule(
            services,
            isEnabled: true,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await repo.GetDueAsync(DateTimeOffset.UtcNow, CancellationToken);

        result.Should().ContainSingle().Which.Id.Should().Be(due.Id);
    }

    [TestMethod]
    public async Task GetDueAsync_WhenDisabled_DoesNotReturnSchedule()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        await PersistSchedule(
            services,
            isEnabled: false,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await repo.GetDueAsync(DateTimeOffset.UtcNow, CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetDueAsync_WhenNextRunInFuture_DoesNotReturnSchedule()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        await PersistSchedule(
            services,
            isEnabled: true,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(30));

        var result = await repo.GetDueAsync(DateTimeOffset.UtcNow, CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateRelationsAsync_WhenEndpointSetChanges_SyncsJunctionOnReload()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        var endpointGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>();
        var suite = await GetSuite(services);

        var endpointA = await endpointGenerator.CreateAsync(CancellationToken);
        var endpointB = await endpointGenerator.CreateAsync(CancellationToken);

        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var schedule = await repo.AddAsync(
            factory("Nightly", suite, [endpointA], TimeSpan.FromHours(1), isEnabled: true, anchorAt: DateTimeOffset.UtcNow),
            CancellationToken);

        // Swap the single endpoint for a different one.
        var updated = await schedule.Update(
            "Nightly", [endpointB], TimeSpan.FromHours(1), isEnabled: true, schedule.AnchorAt, DateTimeOffset.UtcNow,
            CancellationToken);

        var reloaded = await repo.GetAsync(updated.Id, CancellationToken);

        reloaded.Endpoints.Should().ContainSingle().Which.Id.Should().Be(endpointB.Id);
    }

    [TestMethod]
    public async Task UpdateRelationsAsync_WhenEndpointAdded_PersistsBothEndpoints()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        var endpointGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>();
        var suite = await GetSuite(services);

        var endpointA = await endpointGenerator.CreateAsync(CancellationToken);
        var endpointB = await endpointGenerator.CreateAsync(CancellationToken);

        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var schedule = await repo.AddAsync(
            factory("Nightly", suite, [endpointA], TimeSpan.FromHours(1), isEnabled: true, anchorAt: DateTimeOffset.UtcNow),
            CancellationToken);

        var updated = await schedule.Update(
            "Nightly", [endpointA, endpointB], TimeSpan.FromHours(1), isEnabled: true, schedule.AnchorAt,
            DateTimeOffset.UtcNow, CancellationToken);

        var reloaded = await repo.GetAsync(updated.Id, CancellationToken);

        reloaded.Endpoints.Select(e => e.Id)
            .Should().BeEquivalentTo([endpointA.Id, endpointB.Id]);
    }

    // Persists a schedule and then forces its IsEnabled/NextRunAt to the wanted values.
    // CreateNew always sets NextRunAt = CreatedAt + interval (future), so to exercise the
    // "due" query we reconstitute the persisted entity via CreateExisting (reusing it as the
    // IDomainEntityData so the Id/timestamps are preserved) and persist that state.
    private async Task<ITestRunSchedule> PersistSchedule(
        IServiceProvider services,
        bool isEnabled,
        DateTimeOffset nextRunAt)
    {
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestRunSchedule>>();
        var createExisting = services.GetRequiredService<ITestRunSchedule.CreateExisting>();

        var persisted = await generator.CreateAsync(CancellationToken);

        var withState = createExisting(
            persisted.Name,
            persisted.Suite,
            persisted.Endpoints,
            persisted.Interval,
            isEnabled,
            persisted.AnchorAt,
            nextRunAt,
            lastRunAt: null,
            existing: persisted);

        return await repo.UpdateAsync(withState, CancellationToken);
    }

    private async Task<ITestSuite> GetSuite(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().GetOrCreateAsync(CancellationToken);
}
