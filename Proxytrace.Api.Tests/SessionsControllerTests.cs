using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Controllers;
using Proxytrace.Domain.Session;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class SessionsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_ReturnsOwnProjectSessions_SortedByActivityDescending()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ISessionRepository>();
        var projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var older = await RecordSessionAsync(repo, projectId, "sess-older", totalTokens: 10, lastActivityAt: now.AddMinutes(-10));
        var newer = await RecordSessionAsync(repo, projectId, "sess-newer", totalTokens: 20, lastActivityAt: now);

        var controller = ResolveController(services);
        var result = await controller.GetAll(projectId, cancellationToken: CancellationToken);

        result.Items.Select(s => s.Id).Should().Equal(newer, older);
    }

    [TestMethod]
    public async Task GetAll_ForNonMemberProject_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ISessionRepository>();
        var projectId = Guid.NewGuid();
        await RecordSessionAsync(repo, projectId, "sess", totalTokens: 10, lastActivityAt: DateTimeOffset.UtcNow);

        // Caller's scope covers only a different project → the requested project's sessions never leak.
        var controller = ResolveController(services, ScopedGuard(Guid.NewGuid()));
        var result = await controller.GetAll(projectId, cancellationToken: CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [TestMethod]
    public async Task Get_WhenCallerCannotAccessProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ISessionRepository>();
        var projectId = Guid.NewGuid();
        var sessionId = await RecordSessionAsync(repo, projectId, "sess", totalTokens: 10, lastActivityAt: DateTimeOffset.UtcNow);

        var controller = ResolveController(services, DenyingGuard());
        var result = await controller.Get(sessionId, CancellationToken);

        // Existing session, but the guard denies → hidden behind a 404 (no other tenant disclosed).
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_ExistingSession_ReturnsCounters()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ISessionRepository>();
        var projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Two activity records for the same key bump the denormalized counters.
        var sessionId = await RecordSessionAsync(repo, projectId, "sess", totalTokens: 100, lastActivityAt: now.AddMinutes(-1));
        await RecordSessionAsync(repo, projectId, "sess", totalTokens: 40, lastActivityAt: now);

        var controller = ResolveController(services);
        var result = await controller.Get(sessionId, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(sessionId);
        result.Value.ExternalKey.Should().Be("sess");
        result.Value.TraceCount.Should().Be(2);
        result.Value.TotalTokens.Should().Be(140);
    }

    private async Task<Guid> RecordSessionAsync(
        ISessionRepository repo, Guid projectId, string externalKey, long totalTokens, DateTimeOffset lastActivityAt)
    {
        var sessionId = SessionIdDerivation.Derive(projectId, externalKey);
        await repo.RecordActivityAsync(sessionId, externalKey, projectId, totalTokens, lastActivityAt, CancellationToken);
        return sessionId;
    }

    // A non-admin scoped to exactly one project: any other project is inaccessible.
    private static IProjectAccessGuard ScopedGuard(Guid accessibleProjectId)
    {
        var guard = Substitute.For<IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult((Guid?)call.ArgAt<Guid>(0) == accessibleProjectId));
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([accessibleProjectId]));
        return guard;
    }

    // A non-admin who is a member of nothing: every project is inaccessible, scope set is empty.
    private static IProjectAccessGuard DenyingGuard()
    {
        var guard = Substitute.For<IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([]));
        return guard;
    }

    private static SessionsController ResolveController(IServiceProvider services)
        => ResolveController(services, services.GetRequiredService<IProjectAccessGuard>());

    private static SessionsController ResolveController(IServiceProvider services, IProjectAccessGuard guard)
        => new(services.GetRequiredService<ISessionRepository>(), guard);
}
