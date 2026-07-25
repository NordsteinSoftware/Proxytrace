using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Session;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class SessionRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task RecordActivityAsync_UnseenSession_CreatesRow()
    {
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<ISessionRepository>();
        var id = SessionIdDerivation.Derive(project.Id, "run-1");

        await repo.RecordActivityAsync(id, "run-1", project.Id, 50, DateTimeOffset.UtcNow, CancellationToken);

        var session = await repo.FindAsync(id, CancellationToken);
        session.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(session);
        session.ExternalKey.Should().Be("run-1");
        session.TraceCount.Should().Be(1);
        session.TotalTokens.Should().Be(50);
    }

    [TestMethod]
    public async Task RecordActivityAsync_ExistingSession_BumpsCountersAndActivity()
    {
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<ISessionRepository>();
        var id = SessionIdDerivation.Derive(project.Id, "run-1");
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var t2 = t1.AddMinutes(1);

        await repo.RecordActivityAsync(id, "run-1", project.Id, 50, t1, CancellationToken);
        await repo.RecordActivityAsync(id, "run-1", project.Id, 70, t2, CancellationToken);

        var session = await repo.FindAsync(id, CancellationToken);
        session.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(session);
        session.TraceCount.Should().Be(2);
        session.TotalTokens.Should().Be(120);
        session.LastActivityAt.Should().Be(t2);
    }

    [TestMethod]
    public async Task RecordActivityAsync_OutOfOrderOlderActivity_BumpsCountersButKeepsLastActivity()
    {
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<ISessionRepository>();
        var id = SessionIdDerivation.Derive(project.Id, "run-1");
        var newer = DateTimeOffset.UtcNow;
        var older = newer.AddMinutes(-5);

        // A redelivered/out-of-order ingest carries an older CreatedAt: the counters still bump,
        // but LastActivityAt never moves backwards (it would flip the Live indicator off).
        await repo.RecordActivityAsync(id, "run-1", project.Id, 50, newer, CancellationToken);
        await repo.RecordActivityAsync(id, "run-1", project.Id, 70, older, CancellationToken);

        var session = await repo.FindAsync(id, CancellationToken);
        session.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(session);
        session.TraceCount.Should().Be(2);
        session.TotalTokens.Should().Be(120);
        session.LastActivityAt.Should().Be(newer);
    }

    [TestMethod]
    public async Task GetRecentAsync_MultipleSessions_SortsByLastActivityDescendingAndScopesToProject()
    {
        var services = GetServices();
        var projectGen = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var projectA = await projectGen.CreateAsync(CancellationToken);
        var projectB = await projectGen.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<ISessionRepository>();
        var now = DateTimeOffset.UtcNow;

        await repo.RecordActivityAsync(SessionIdDerivation.Derive(projectA.Id, "old"), "old", projectA.Id, 1, now.AddHours(-2), CancellationToken);
        await repo.RecordActivityAsync(SessionIdDerivation.Derive(projectA.Id, "new"), "new", projectA.Id, 1, now, CancellationToken);
        await repo.RecordActivityAsync(SessionIdDerivation.Derive(projectB.Id, "other"), "other", projectB.Id, 1, now, CancellationToken);

        var (items, total) = await repo.GetRecentAsync(projectA.Id, 1, 10, CancellationToken);

        total.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].ExternalKey.Should().Be("new");
        items[1].ExternalKey.Should().Be("old");
    }
}
