using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.CostControl;
using Proxytrace.Application.CostControl.Internal;
using Proxytrace.Application.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Common.Serialization;
using Proxytrace.Common.Time;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Statistics;
using Proxytrace.Licensing;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class CostBudgetGuardTests : BaseTest<Module>
{
    /// <summary>A test clock whose time can be advanced deterministically.</summary>
    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset start) => UtcNow = start;

        public DateTimeOffset UtcNow { get; set; }
    }

    private static ILicenseService LicensedService(bool licensed = true)
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(LicenseFeature.CostControls).Returns(licensed);
        return license;
    }

    private static ICostStatistics SpendOf(
        ProjectAgentCostStat[] rows,
        ProjectApiKeyCostStat[]? keyRows = null)
    {
        var stats = Substitute.For<ICostStatistics>();
        stats.GetMonthToDateSpendAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProjectAgentCostStat>>(rows));
        // Always stubbed, even when empty: an unstubbed Task-returning member hands back a task
        // whose result is null, which would NRE the moment a key-scoped limit made the guard ask.
        stats.GetMonthToDateSpendByApiKeyAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProjectApiKeyCostStat>>(keyRows ?? []));
        return stats;
    }

    [TestMethod]
    public async Task Evaluate_WhenSpendCrossesSoftLimit_RaisesWarningOnce()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        ICostLimit limit = await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m);

        CostBudgetGuard guard = BuildGuard(services, new ProjectAgentCostStat(project.Id, agent.Id, 60m));

        await guard.EvaluateAsync(CancellationToken);
        await guard.EvaluateAsync(CancellationToken);

        // Fires exactly once per threshold per month, even though the second tick sees the same
        // over-budget spend.
        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null
                && r.Kind == NotificationKind.CostBudget
                && r.Severity == NotificationSeverity.Warning),
            Arg.Any<CancellationToken>());

        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IReadOnlyList<ICostLimitBreach> recorded =
            await breaches.GetForMonthAsync(CostMonth.StartOf(clock.UtcNow), CancellationToken);
        recorded.Should().ContainSingle()
            .Which.Should().Match<ICostLimitBreach>(b =>
                b.Threshold == CostThreshold.Soft && b.CostLimit.Id == limit.Id);
    }

    [TestMethod]
    public async Task Evaluate_WhenSpendCrossesHardLimit_RaisesCriticalAndRecordsHardBreach()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m);

        await BuildGuard(services, new ProjectAgentCostStat(project.Id, agent.Id, 150m))
            .EvaluateAsync(CancellationToken);

        // A single tick that vaults past both thresholds tells the whole story: warning then critical.
        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.Severity == NotificationSeverity.Warning),
            Arg.Any<CancellationToken>());
        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.Severity == NotificationSeverity.Critical),
            Arg.Any<CancellationToken>());

        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IReadOnlyList<ICostLimitBreach> recorded =
            await breaches.GetForMonthAsync(CostMonth.StartOf(clock.UtcNow), CancellationToken);
        recorded.Should().HaveCount(2);
        recorded.Should().ContainSingle(b => b.Threshold == CostThreshold.Hard);
    }

    [TestMethod]
    public async Task Evaluate_BudgetAlerts_CarryNoNotificationTarget()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m);

        await BuildGuard(services, new ProjectAgentCostStat(project.Id, agent.Id, 150m))
            .EvaluateAsync(CancellationToken);

        // De-duplication in NotificationService is target-scoped but kind-INSENSITIVE, so carrying a
        // target would let an unacknowledged soft alert swallow the later hard alert.
        await notifications.Received(2).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.TargetKind == null && r.TargetId == null),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Evaluate_WhenUnlicensed_DoesNothing()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService(licensed: false)).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m);

        await BuildGuard(services, new ProjectAgentCostStat(project.Id, agent.Id, 999m))
            .EvaluateAsync(CancellationToken);

        // Use-time degrade: the configuration survives, nothing fires and nothing blocks.
        await notifications.DidNotReceive().NotifyAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        (await breaches.GetForMonthAsync(CostMonth.StartOf(clock.UtcNow), CancellationToken))
            .Should().BeEmpty();
    }

    [TestMethod]
    public async Task Evaluate_WhenLimitDisabled_DoesNotFire()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m, enabled: false);

        await BuildGuard(services, new ProjectAgentCostStat(project.Id, agent.Id, 999m))
            .EvaluateAsync(CancellationToken);

        await notifications.DidNotReceive().NotifyAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Evaluate_AfterBreachStateCleared_FiresAgain()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        ICostLimit limit = await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m);

        CostBudgetGuard guard = BuildGuard(services, new ProjectAgentCostStat(project.Id, agent.Id, 60m));
        await guard.EvaluateAsync(CancellationToken);

        // Editing a limit clears its breach state (what the PUT endpoint does), so the next tick
        // re-evaluates against the new thresholds and re-announces.
        await services.GetRequiredService<ICostLimitBreachRepository>()
            .DeleteForLimitAsync(limit.Id, CancellationToken);
        await guard.EvaluateAsync(CancellationToken);

        await notifications.Received(2).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.Severity == NotificationSeverity.Warning),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Evaluate_AfterMonthRollover_ReArmsTheAlert()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m);

        CostBudgetGuard guard = BuildGuard(services, new ProjectAgentCostStat(project.Id, agent.Id, 60m));
        await guard.EvaluateAsync(CancellationToken);

        // Nothing is cleaned up on the 1st: the new month simply has no breach row yet.
        clock.UtcNow = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);
        await guard.EvaluateAsync(CancellationToken);

        await notifications.Received(2).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.Severity == NotificationSeverity.Warning),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Evaluate_AgentScopedLimit_MeasuresOnlyThatAgentsSpend()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        IAgent other = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("Other agent", cancellationToken: CancellationToken);

        await AddLimitAsync(services, project, agent, soft: 50m, hard: null);

        // The other agent is far over the threshold; the scoped agent is not, so nothing fires.
        await BuildGuard(
                services,
                new ProjectAgentCostStat(project.Id, agent.Id, 10m),
                new ProjectAgentCostStat(project.Id, other.Id, 500m))
            .EvaluateAsync(CancellationToken);

        await notifications.DidNotReceive().NotifyAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Evaluate_ProjectLimit_SumsEveryAgentsSpend()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        IAgent other = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("Other agent", cancellationToken: CancellationToken);

        await AddLimitAsync(services, project, agent: null, soft: 50m, hard: null);

        // Neither agent alone crosses 50; together they do — agent spend counts toward the project.
        await BuildGuard(
                services,
                new ProjectAgentCostStat(project.Id, agent.Id, 30m),
                new ProjectAgentCostStat(project.Id, other.Id, 25m))
            .EvaluateAsync(CancellationToken);

        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null && r.Severity == NotificationSeverity.Warning),
            Arg.Any<CancellationToken>());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Evaluate_WhenKeyScopedSpendCrossesHardLimit_RecordsHardBreachForThatKey()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        IApiKey apiKey = await services.GetRequiredService<IDomainEntityGenerator<IApiKey>>()
            .GetOrCreateAsync(CancellationToken);
        ICostLimit limit = await AddLimitAsync(
            services, apiKey.Project, agent: null, soft: null, hard: 25m, apiKey: apiKey);

        CostBudgetGuard guard = BuildGuard(
            services,
            spend: [],
            keySpend: [new ProjectApiKeyCostStat(apiKey.Project.Id, apiKey.Id, 30m)]);

        await guard.EvaluateAsync(CancellationToken);

        await notifications.Received(1).NotifyAsync(
            Arg.Is<NotificationRequest>(r => r != null
                && r.Kind == NotificationKind.CostBudget
                && r.Severity == NotificationSeverity.Critical),
            Arg.Any<CancellationToken>());

        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        IReadOnlyList<ICostLimitBreach> recorded =
            await breaches.GetForMonthAsync(CostMonth.StartOf(clock.UtcNow), CancellationToken);
        recorded.Should().ContainSingle()
            .Which.Should().Match<ICostLimitBreach>(b =>
                b.Threshold == CostThreshold.Hard && b.CostLimit.Id == limit.Id);
    }

    [TestMethod]
    public async Task Evaluate_KeyScopedLimit_IgnoresSpendOfOtherKeys()
    {
        var notifications = Substitute.For<INotificationService>();
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(notifications).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        var keyGenerator = services.GetRequiredService<IDomainEntityGenerator<IApiKey>>();
        IApiKey budgeted = await keyGenerator.GetOrCreateAsync(CancellationToken);
        await AddLimitAsync(
            services, budgeted.Project, agent: null, soft: null, hard: 25m, apiKey: budgeted);

        // Spend belongs to a different key of the same project, plus the unattributed group.
        CostBudgetGuard guard = BuildGuard(
            services,
            spend: [],
            keySpend:
            [
                new ProjectApiKeyCostStat(budgeted.Project.Id, Guid.NewGuid(), 500m),
                new ProjectApiKeyCostStat(budgeted.Project.Id, null, 500m),
            ]);

        await guard.EvaluateAsync(CancellationToken);

        await notifications.DidNotReceive().NotifyAsync(
            Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Evaluate_WithNoKeyScopedLimit_NeverQueriesKeySpend()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(Substitute.For<INotificationService>()).As<INotificationService>();
            builder.RegisterInstance(LicensedService()).As<ILicenseService>();
            builder.RegisterInstance(clock).As<IClock>();
        });

        (IProject project, IAgent agent) = await SeedAsync(services);
        await AddLimitAsync(services, project, agent: null, soft: 50m, hard: 100m);

        var stats = SpendOf([new ProjectAgentCostStat(project.Id, agent.Id, 60m)]);
        CostBudgetGuard guard = BuildGuard(services, stats);

        await guard.EvaluateAsync(CancellationToken);

        // The per-key aggregate is an extra scan of the highest-volume table; an install with no
        // key budgets must keep the exact tick cost it had before key scope existed.
        await stats.DidNotReceive().GetMonthToDateSpendByApiKeyAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private async Task<(IProject Project, IAgent Agent)> SeedAsync(IServiceProvider services)
    {
        IAgent agent = await services.GetRequiredService<IAgentGenerator>().GetOrCreateAsync(CancellationToken);
        return (agent.Project, agent);
    }

    private async Task<ICostLimit> AddLimitAsync(
        IServiceProvider services,
        IProject project,
        IAgent? agent,
        decimal? soft,
        decimal? hard,
        bool enabled = true,
        IApiKey? apiKey = null)
    {
        var factory = services.GetRequiredService<ICostLimit.CreateNew>();
        var repository = services.GetRequiredService<ICostLimitRepository>();
        return await repository.AddAsync(factory(project, agent, apiKey, soft, hard, enabled), CancellationToken);
    }

    /// <summary>
    /// Builds the guard against a stubbed spend source. The guard is constructed by hand rather
    /// than resolved so each test states exactly what month-to-date spend it sees, instead of
    /// seeding thousands of priced traces to reach a threshold.
    /// </summary>
    private static CostBudgetGuard BuildGuard(IServiceProvider services, params ProjectAgentCostStat[] spend)
        => BuildGuard(services, SpendOf(spend));

    private static CostBudgetGuard BuildGuard(
        IServiceProvider services,
        ProjectAgentCostStat[] spend,
        ProjectApiKeyCostStat[]? keySpend)
        => BuildGuard(services, SpendOf(spend, keySpend));

    private static CostBudgetGuard BuildGuard(IServiceProvider services, ICostStatistics costStatistics)
        => new(
            costStatistics,
            services.GetRequiredService<ICostLimitRepository>(),
            services.GetRequiredService<ICostLimitBreachRepository>(),
            services.GetRequiredService<ICostLimitBreach.CreateNew>(),
            services.GetRequiredService<INotificationService>(),
            services.GetRequiredService<ILicenseService>(),
            services.GetRequiredService<ISerializer>(),
            services.GetRequiredService<IClock>(),
            new CostControlOptions(),
            NullLogger<Audit>.Instance,
            NullLogger<CostBudgetGuard>.Instance);
}
