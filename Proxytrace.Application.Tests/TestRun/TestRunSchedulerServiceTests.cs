using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.TestRun;
using Proxytrace.Application.TestRun.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.TestRun;

[TestClass]
public sealed class TestRunSchedulerServiceTests : BaseTest<Module>
{
    private static ILicenseService LicenseWith(bool scheduledEnabled)
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(LicenseFeature.ScheduledTestRuns).Returns(scheduledEnabled);
        return license;
    }

    private static async Task<ITestRunSchedule> CreateDueScheduleAsync(IServiceProvider services, CancellationToken ct)
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestRunSchedule>>();
        return await generator.CreateAsync(ct);
    }

    [TestMethod]
    public async Task RunDueSchedulesAsync_WhenDueEnabledAndLicensed_FiresRunAndAdvancesNextRun()
    {
        var license = LicenseWith(scheduledEnabled: true);
        var runner = Substitute.For<ITestRunnerService>();

        IServiceProvider services = GetServices(b =>
        {
            b.RegisterInstance(license).As<ILicenseService>();
            b.RegisterInstance(runner).As<ITestRunnerService>();
        });

        var schedule = await CreateDueScheduleAsync(services, CancellationToken);
        var scheduleRepo = services.GetRequiredService<ITestRunScheduleRepository>();
        var service = services.GetRequiredService<TestRunSchedulerService>();

        // now is well past NextRunAt (NextRunAt = CreatedAt + interval), so the schedule is due.
        var now = DateTimeOffset.UtcNow.AddDays(7);
        await service.RunDueSchedulesAsync(now, CancellationToken);

        await runner.Received(1).RunInBackgroundAsync(
            Arg.Any<ITestSuite>(),
            Arg.Any<IReadOnlyList<IModelEndpoint>>(),
            schedule.Id,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        var reloaded = await scheduleRepo.FindAsync(schedule.Id, CancellationToken);
        reloaded.Should().NotBeNull();
        reloaded.NextRunAt.Should().BeAfter(now);
        reloaded.LastRunAt.Should().Be(now);
    }

    [TestMethod]
    public async Task RunDueSchedulesAsync_WhenPriorRunStillActive_DoesNotFire()
    {
        var license = LicenseWith(scheduledEnabled: true);
        var runner = Substitute.For<ITestRunnerService>();

        IServiceProvider services = GetServices(b =>
        {
            b.RegisterInstance(license).As<ILicenseService>();
            b.RegisterInstance(runner).As<ITestRunnerService>();
        });

        var schedule = await CreateDueScheduleAsync(services, CancellationToken);

        // Persist an in-flight (Running) group attributed to this schedule.
        var createGroup = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var groupRepo = services.GetRequiredService<ITestRunGroupRepository>();
        var group = createGroup(schedule.Suite, false, schedule.Id, sampleCount: 1);
        group = await groupRepo.AddAsync(group, CancellationToken);
        await group.SetRunning(CancellationToken);

        var service = services.GetRequiredService<TestRunSchedulerService>();
        var now = DateTimeOffset.UtcNow.AddDays(7);
        await service.RunDueSchedulesAsync(now, CancellationToken);

        await runner.DidNotReceive().RunInBackgroundAsync(
            Arg.Any<ITestSuite>(),
            Arg.Any<IReadOnlyList<IModelEndpoint>>(),
            Arg.Any<Guid?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunDueSchedulesAsync_WhenFeatureNotLicensed_DoesNotFireAndKeepsSchedule()
    {
        var license = LicenseWith(scheduledEnabled: false);
        var runner = Substitute.For<ITestRunnerService>();

        IServiceProvider services = GetServices(b =>
        {
            b.RegisterInstance(license).As<ILicenseService>();
            b.RegisterInstance(runner).As<ITestRunnerService>();
        });

        var schedule = await CreateDueScheduleAsync(services, CancellationToken);
        var scheduleRepo = services.GetRequiredService<ITestRunScheduleRepository>();
        var service = services.GetRequiredService<TestRunSchedulerService>();

        var now = DateTimeOffset.UtcNow.AddDays(7);
        await service.RunDueSchedulesAsync(now, CancellationToken);

        await runner.DidNotReceive().RunInBackgroundAsync(
            Arg.Any<ITestSuite>(),
            Arg.Any<IReadOnlyList<IModelEndpoint>>(),
            Arg.Any<Guid?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        var reloaded = await scheduleRepo.FindAsync(schedule.Id, CancellationToken);
        reloaded.Should().NotBeNull();
    }
}
