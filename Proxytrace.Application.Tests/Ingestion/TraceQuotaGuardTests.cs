using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Ingestion.Internal;
using Proxytrace.Application.Notifications;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Proxytrace.Licensing;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests.Ingestion;

[TestClass]
public sealed class TraceQuotaGuardTests : BaseTest<Module>
{
    private static IAgentCallRepository RepositoryWithTotal(int total)
    {
        var repo = Substitute.For<IAgentCallRepository>();
        repo.GetFilteredAsync(Arg.Any<AgentCallFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<IAgentCall> Items, int Total)>(([], total)));
        return repo;
    }

    private (TraceQuotaGuard Guard, IAgentCallRepository Repo) BuildGuard(
        ILicenseService license, IAgentCallRepository repo)
    {
        var services = GetServices(b =>
        {
            b.RegisterInstance(license).As<ILicenseService>();
            b.RegisterInstance(repo).As<IAgentCallRepository>();
        });

        return (services.GetRequiredService<TraceQuotaGuard>(), repo);
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10, CancellationToken);
    }

    private static bool CountWasRead(IAgentCallRepository repo) => repo.ReceivedCalls().Any();

    private static bool ProjectScopedCountWasRead(IAgentCallRepository repo)
        => repo.ReceivedCalls()
            .SelectMany(c => c.GetArguments())
            .OfType<AgentCallFilter>()
            .Any(f => f.ProjectId is not null);

    [TestMethod]
    public async Task IsCurrentMonthOverQuota_WhenTotalAtLimit_ReturnsTrue()
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(LicenseLimit.MaxTracesPerMonth).Returns(5);
        var (guard, _) = BuildGuard(license, RepositoryWithTotal(5));

        await guard.StartAsync(CancellationToken);
        try
        {
            await WaitUntilAsync(() => guard.IsCurrentMonthOverQuota);
            guard.IsCurrentMonthOverQuota.Should().BeTrue();
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task IsCurrentMonthOverQuota_WhenTotalBelowLimit_ReturnsFalse()
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(LicenseLimit.MaxTracesPerMonth).Returns(5);
        var (guard, repo) = BuildGuard(license, RepositoryWithTotal(3));

        await guard.StartAsync(CancellationToken);
        try
        {
            await WaitUntilAsync(() => CountWasRead(repo));
            guard.IsCurrentMonthOverQuota.Should().BeFalse();
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }

    // ── per-project fair share ───────────────────────────────────────────────────────────
    //
    // The cap is installation-wide, but enforcing it as a single global switch meant one busy
    // project could consume the whole month's allowance and silently stop capture for every other
    // project — and nothing surfaced that captures were being dropped at all.

    /// <summary>A repository whose month count varies by the filter's project.</summary>
    private static IAgentCallRepository RepositoryWithProjectTotals(
        int installTotal,
        IReadOnlyDictionary<Guid, int> perProject)
    {
        var repo = Substitute.For<IAgentCallRepository>();
        repo.GetFilteredAsync(Arg.Any<AgentCallFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var filter = call.Arg<AgentCallFilter>();
                ArgumentNullException.ThrowIfNull(filter);
                int total = filter.ProjectId is { } id ? perProject.GetValueOrDefault(id) : installTotal;
                return Task.FromResult<(IReadOnlyList<IAgentCall> Items, int Total)>(([], total));
            });
        return repo;
    }

    private static IProjectRepository ProjectsNamed(IEnumerable<Guid> ids)
    {
        // The project substitutes are stubbed BEFORE the Returns() below: stubbing one substitute
        // inside another's Returns() argument leaves NSubstitute unable to tell which call it is
        // returning from.
        var projects = new List<IProject>();
        foreach (Guid id in ids)
        {
            var project = Substitute.For<IProject>();
            project.Id.Returns(id);
            projects.Add(project);
        }

        var repo = Substitute.For<IProjectRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IProject>>(projects));
        return repo;
    }

    private async Task<TraceQuotaGuard> RunToFirstRecomputeAsync(
        long limit,
        int installTotal,
        IReadOnlyDictionary<Guid, int> perProject,
        bool expectOverQuota,
        INotificationService? notifications = null)
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(LicenseLimit.MaxTracesPerMonth).Returns(limit);
        var repo = RepositoryWithProjectTotals(installTotal, perProject);

        var projectRepo = ProjectsNamed(perProject.Keys);
        var services = GetServices(b =>
        {
            b.RegisterInstance(license).As<ILicenseService>();
            b.RegisterInstance(repo).As<IAgentCallRepository>();
            b.RegisterInstance(projectRepo).As<IProjectRepository>();
            if (notifications is not null)
                b.RegisterInstance(notifications).As<INotificationService>();
        });

        var guard = services.GetRequiredService<TraceQuotaGuard>();
        await guard.StartAsync(CancellationToken);

        // The recompute runs on the background loop, so wait for evidence it has landed rather than
        // for a fixed delay. Over-quota is directly observable; under it, the per-project counts are
        // read only after the installation total, so a project-filtered read proves the pass got
        // past the point that would have set the flag.
        await WaitUntilAsync(() => expectOverQuota
            ? guard.IsCurrentMonthOverQuota
            : ProjectScopedCountWasRead(repo));

        return guard;
    }

    [TestMethod]
    public async Task IsOverQuota_AtTheLimit_ThrottlesOnlyTheProjectAboveItsShare()
    {
        // THE regression. A global switch stopped capture for the quiet project too, even though it
        // had consumed 10 of a 500-trace share. The cap still binds — the busy project is throttled.
        var busy = Guid.NewGuid();
        var quiet = Guid.NewGuid();

        var guard = await RunToFirstRecomputeAsync(
            limit: 1000, installTotal: 1000,
            perProject: new Dictionary<Guid, int> { [busy] = 990, [quiet] = 10 },
            expectOverQuota: true);

        try
        {
            guard.IsCurrentMonthOverQuota.Should().BeTrue();
            guard.IsOverQuota(busy).Should().BeTrue("it is above its 500-trace share of the cap");
            guard.IsOverQuota(quiet).Should().BeFalse("it has consumed 10 of its 500-trace share");
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task IsOverQuota_AtTheLimit_ThrottlesEveryProjectAboveItsShare()
    {
        // The limit must still bind when the overrun is shared out evenly.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var guard = await RunToFirstRecomputeAsync(
            limit: 1000, installTotal: 1000,
            perProject: new Dictionary<Guid, int> { [a] = 500, [b] = 500 },
            expectOverQuota: true);

        try
        {
            guard.IsOverQuota(a).Should().BeTrue();
            guard.IsOverQuota(b).Should().BeTrue();
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task IsOverQuota_BelowTheLimit_ThrottlesNobody()
    {
        var busy = Guid.NewGuid();
        var quiet = Guid.NewGuid();

        var guard = await RunToFirstRecomputeAsync(
            limit: 1000, installTotal: 400,
            perProject: new Dictionary<Guid, int> { [busy] = 390, [quiet] = 10 },
            expectOverQuota: false);

        try
        {
            guard.IsCurrentMonthOverQuota.Should().BeFalse();
            guard.IsOverQuota(busy).Should().BeFalse();
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task IsOverQuota_ForAProjectWithNoTraces_IsFalseEvenAtTheLimit()
    {
        // Covers a project created since the last recompute: it has consumed nothing.
        var busy = Guid.NewGuid();

        var guard = await RunToFirstRecomputeAsync(
            limit: 100, installTotal: 100,
            perProject: new Dictionary<Guid, int> { [busy] = 100 },
            expectOverQuota: true);

        try
        {
            guard.IsOverQuota(Guid.NewGuid()).Should().BeFalse();
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task WhenAProjectStartsBeingThrottled_ANotificationIsRaisedForItAlone()
    {
        // A dropped capture is still acknowledged to the client — failing the proxied call would
        // take the caller's application down over a billing limit — so without this the only symptom
        // was traces quietly going missing.
        var busy = Guid.NewGuid();
        var quiet = Guid.NewGuid();
        var notifications = Substitute.For<INotificationService>();

        var guard = await RunToFirstRecomputeAsync(
            limit: 1000, installTotal: 1000,
            perProject: new Dictionary<Guid, int> { [busy] = 990, [quiet] = 10 },
            expectOverQuota: true,
            notifications: notifications);

        try
        {
            await WaitUntilAsync(() => notifications.ReceivedCalls().Any());

            await notifications.Received(1).NotifyAsync(
                Arg.Is<NotificationRequest>(r =>
                    r != null && r.Kind == NotificationKind.TraceQuotaReached && r.ProjectId == busy),
                Arg.Any<CancellationToken>());

            await notifications.DidNotReceive().NotifyAsync(
                Arg.Is<NotificationRequest>(r => r != null && r.ProjectId == quiet),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }

    [TestMethod]
    public async Task IsCurrentMonthOverQuota_WhenLimitUnlimited_ReturnsFalseWithoutCounting()
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(LicenseLimit.MaxTracesPerMonth).Returns(long.MaxValue);
        var (guard, repo) = BuildGuard(license, RepositoryWithTotal(int.MaxValue));

        // Unlimited tiers short-circuit before any await, so the first recompute completes
        // synchronously during StartAsync.
        await guard.StartAsync(CancellationToken);
        try
        {
            guard.IsCurrentMonthOverQuota.Should().BeFalse();
            await repo.DidNotReceive().GetFilteredAsync(
                Arg.Any<AgentCallFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await guard.StopAsync(CancellationToken);
        }
    }
}
