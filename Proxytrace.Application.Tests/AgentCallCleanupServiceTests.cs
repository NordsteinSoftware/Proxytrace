using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Cleanup;
using Proxytrace.Application.Cleanup.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Scope;
using Proxytrace.Domain.Session;
using Proxytrace.Licensing;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class AgentCallCleanupServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CleanOnce_2DaysRetention_RemovesEntriesOlderThan2Days()
    {
        const int retentionDurationDays = 2;
        var retentionDuration = TimeSpan.FromDays(retentionDurationDays);

        var agentCallRepository = Substitute.For<IAgentCallRepository>();
        var services = GetServices(builder =>
        {
            builder.RegisterInstance(agentCallRepository).As<IAgentCallRepository>();

            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = retentionDurationDays,
            });
        });

        var service = services.GetRequiredService<AgentCallCleanupService>();
        await service.CleanOnceAsync(CancellationToken);

        await agentCallRepository.Received(1)
            .RemoveOlderThanAsync(
                Arg.Is<DateTimeOffset>(x => DateTimeOffset.UtcNow - retentionDuration - x < TimeSpan.FromSeconds(10)),
                Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void CleanOnce_NegativeRetentionPeriod_Throws()
    {
        const int retentionDurationDays = -1;
        var services = GetServices(builder =>
        {
            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = retentionDurationDays,
            });
        });

        FluentActions.Invoking(services.GetRequiredService<AgentCallCleanupService>)
            .Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CleanOnce_ValidRetentionPeriod_RemovesEntriesOlderThan()
    {
        const int callGenerated = 10;
        const int retentionDurationDays = callGenerated + 1;
        var services = GetServices(builder =>
        {
            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = retentionDurationDays,
            });
        });
        var generator = services.GetRequiredService<IAgentCallGenerator>();
        var repository = services.GetRequiredService<IRepository<IAgentCall>>();
        var service = services.GetRequiredService<AgentCallCleanupService>();

        HashSet<Guid> expectedDeleted = [];
        for (var i = 0; i < callGenerated; i++)
        {
            var call1 = await generator.CreateAsync(
                DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDurationDays + i + 1), CancellationToken);
            expectedDeleted.Add(call1.Id);

            await generator.CreateAsync(DateTimeOffset.UtcNow - TimeSpan.FromDays(i + 1), CancellationToken);
        }

        var allBeforeDelete = await repository.GetAllAsync(CancellationToken);
        allBeforeDelete.Should().HaveCount(callGenerated * 2);

        await service.CleanOnceAsync(CancellationToken);

        var allAfterDelete = await repository.GetAllAsync(CancellationToken);
        allAfterDelete.Should().HaveCount(callGenerated);

        allAfterDelete.Should().AllSatisfy(x => expectedDeleted.Should().NotContain(x.Id));
    }

    [TestMethod]
    public async Task CleanOnce_ReconcilesScopeMembershipCountersAndSweepsMembershipsWithTheSameCutoff()
    {
        const int retentionDurationDays = 2;
        var expectedCutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDurationDays);
        var removals = new[] { new ScopeTraceRemoval(Guid.NewGuid(), Guid.NewGuid(), 4, 400) };

        var agentCallRepository = Substitute.For<IAgentCallRepository>();
        agentCallRepository
            .GetScopeRemovalsOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(removals);
        var scopeRepository = Substitute.For<IScopeRepository>();

        var services = GetServices(builder =>
        {
            builder.RegisterInstance(agentCallRepository).As<IAgentCallRepository>();
            builder.RegisterInstance(scopeRepository).As<IScopeRepository>();
            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = retentionDurationDays,
            });
        });

        await services.GetRequiredService<AgentCallCleanupService>().CleanOnceAsync(CancellationToken);

        var tolerance = TimeSpan.FromSeconds(10);

        // Deltas are read before the delete, then handed back to the membership counters.
        Received.InOrder(() =>
        {
            agentCallRepository.GetScopeRemovalsOlderThanAsync(
                Arg.Is<DateTimeOffset>(x => (expectedCutoff - x).Duration() < tolerance), Arg.Any<CancellationToken>());
            agentCallRepository.RemoveOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
            scopeRepository.RecordTraceRemovalsAsync(Arg.Any<IReadOnlyCollection<ScopeTraceRemoval>>(), Arg.Any<CancellationToken>());
        });

        await scopeRepository.Received(1).RecordTraceRemovalsAsync(
            Arg.Is<IReadOnlyCollection<ScopeTraceRemoval>>(x => x != null && x.SequenceEqual(removals)),
            Arg.Any<CancellationToken>());
        await scopeRepository.Received(1).RemoveMembershipsOlderThanAsync(
            Arg.Is<DateTimeOffset>(x => (expectedCutoff - x).Duration() < tolerance),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task CleanOnce_SweepsIdleScopesWithTheSameCutoff_AfterTheirMemberships()
    {
        // Without this sweep every scope ever admitted would hold a slot under the per-project cap
        // forever. It must run after the membership sweep, which is what makes a scope idle.
        const int retentionDurationDays = 2;
        var expectedCutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDurationDays);
        var scopeRepository = Substitute.For<IScopeRepository>();

        var services = GetServices(builder =>
        {
            builder.RegisterStub<IAgentCallRepository>();
            builder.RegisterInstance(scopeRepository).As<IScopeRepository>();
            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = retentionDurationDays,
            });
        });

        await services.GetRequiredService<AgentCallCleanupService>().CleanOnceAsync(CancellationToken);

        var tolerance = TimeSpan.FromSeconds(10);
        Received.InOrder(() =>
        {
            scopeRepository.RemoveMembershipsOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
            scopeRepository.RemoveIdleOlderThanAsync(
                Arg.Is<DateTimeOffset>(x => (expectedCutoff - x).Duration() < tolerance), Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task CleanOnce_ReconcilesSessionCountersAndSweepsSessionsWithTheSameCutoff()
    {
        // Retention deleted traces without touching the sessions that grouped them, so a session
        // header kept claiming traces its timeline could no longer show, and session rows never
        // aged out at all (#436).
        const int retentionDurationDays = 2;
        var expectedCutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDurationDays);
        var removals = new[] { new SessionTraceRemoval(Guid.NewGuid(), 3, 300) };

        var agentCallRepository = Substitute.For<IAgentCallRepository>();
        agentCallRepository
            .GetSessionRemovalsOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(removals);
        var sessionRepository = Substitute.For<ISessionRepository>();

        var services = GetServices(builder =>
        {
            builder.RegisterInstance(agentCallRepository).As<IAgentCallRepository>();
            builder.RegisterInstance(sessionRepository).As<ISessionRepository>();
            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = retentionDurationDays,
            });
        });

        await services.GetRequiredService<AgentCallCleanupService>().CleanOnceAsync(CancellationToken);

        var tolerance = TimeSpan.FromSeconds(10);

        // The deltas must be read before the delete — afterwards the rows are gone.
        Received.InOrder(() =>
        {
            agentCallRepository.GetSessionRemovalsOlderThanAsync(
                Arg.Is<DateTimeOffset>(x => (expectedCutoff - x).Duration() < tolerance), Arg.Any<CancellationToken>());
            agentCallRepository.RemoveOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
            sessionRepository.RecordTraceRemovalsAsync(Arg.Any<IReadOnlyCollection<SessionTraceRemoval>>(), Arg.Any<CancellationToken>());
        });

        await sessionRepository.Received(1).RecordTraceRemovalsAsync(
            Arg.Is<IReadOnlyCollection<SessionTraceRemoval>>(x => x != null && x.SequenceEqual(removals)),
            Arg.Any<CancellationToken>());

        // The session sweep rides the same cutoff: a session's last activity IS its newest trace.
        await sessionRepository.Received(1).RemoveOlderThanAsync(
            Arg.Is<DateTimeOffset>(x => (expectedCutoff - x).Duration() < tolerance),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task CleanOnce_DeleteThrows_Exception()
    {
        const int retentionDurationDays = 1;
        var agentCallRepository = Substitute.For<IAgentCallRepository>();
        var services = GetServices(builder =>
        {
            builder.RegisterInstance(agentCallRepository).As<IAgentCallRepository>();

            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = retentionDurationDays,
            });
        });

        var service = services.GetRequiredService<AgentCallCleanupService>();
        agentCallRepository.When(x => x.RemoveOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()))
            .Throws(new Exception());

        await FluentActions.Invoking(() => service.CleanOnceAsync(CancellationToken)).Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task CleanOnce_LicenseCapBelowConfiguredRetention_UsesLicenseCap()
    {
        const int configuredRetentionDays = 100;
        const long licenseCapDays = 2;

        var license = Substitute.For<ILicenseService>();
        license.GetLimit(LicenseLimit.TraceRetentionDays).Returns(licenseCapDays);

        var agentCallRepository = Substitute.For<IAgentCallRepository>();
        var services = GetServices(builder =>
        {
            builder.RegisterInstance(license).As<ILicenseService>();
            builder.RegisterInstance(agentCallRepository).As<IAgentCallRepository>();
            builder.RegisterInstance(new AgentCallCleanupConfiguration
            {
                CleanupIntervalHours = 1,
                RetentionDurationDays = configuredRetentionDays,
            });
        });

        var service = services.GetRequiredService<AgentCallCleanupService>();
        await service.CleanOnceAsync(CancellationToken);

        // The license cap (2 days) must win over the larger configured retention (100 days).
        var cap = TimeSpan.FromDays(licenseCapDays);
        await agentCallRepository.Received(1)
            .RemoveOlderThanAsync(
                Arg.Is<DateTimeOffset>(x => DateTimeOffset.UtcNow - cap - x < TimeSpan.FromSeconds(10)),
                Arg.Any<CancellationToken>());
    }
}
