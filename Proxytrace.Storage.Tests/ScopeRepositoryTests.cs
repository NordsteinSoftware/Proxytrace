using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Testing;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class ScopeRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AdmitAsync_UnseenKey_CreatesScopeUnderDerivedId()
    {
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();

        var scopeId = await repo.AdmitAsync(project.Id, "support", 10, CancellationToken);

        scopeId.Should().Be(ScopeIdDerivation.Derive(project.Id, "support"));
        var scope = await repo.GetAsync(ScopeIdDerivation.Derive(project.Id, "support"), CancellationToken);
        scope.ExternalKey.Should().Be("support");
        scope.ProjectId.Should().Be(project.Id);
    }

    [TestMethod]
    public async Task AdmitAsync_SameKeyTwice_CreatesOneScope()
    {
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();

        var first = await repo.AdmitAsync(project.Id, "support", 10, CancellationToken);
        var second = await repo.AdmitAsync(project.Id, "support", 10, CancellationToken);

        second.Should().Be(first);
        (await repo.GetOverviewsAsync(project.Id, CancellationToken)).Should().ContainSingle();
    }

    [TestMethod]
    public async Task AdmitAsync_KeyOfScopeWithForeignId_ReturnsExistingScopeId()
    {
        // A scope created through the domain factory carries a random id, not the derived one —
        // admission must still resolve the key to that row instead of tripping the unique index.
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var existing = await services.GetRequiredService<IScope.CreateNew>()("support", project.Id).AddAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();

        var scopeId = await repo.AdmitAsync(project.Id, "support", 10, CancellationToken);

        scopeId.Should().Be(existing.Id);
    }

    [TestMethod]
    public async Task AdmitAsync_ProjectAtCap_ReturnsNullForNewKeyButAdmitsKnownKey()
    {
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var known = await repo.AdmitAsync(project.Id, "a", 2, CancellationToken);
        await repo.AdmitAsync(project.Id, "b", 2, CancellationToken);

        var overCap = await repo.AdmitAsync(project.Id, "c", 2, CancellationToken);
        var again = await repo.AdmitAsync(project.Id, "a", 2, CancellationToken);

        overCap.Should().BeNull();
        again.Should().Be(known);
    }

    [TestMethod]
    public async Task AdmitAsync_CapIsPerProject_OtherProjectStillAdmits()
    {
        var services = GetServices();
        var projects = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var full = await projects.CreateAsync(CancellationToken);
        var other = await projects.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        await repo.AdmitAsync(full.Id, "a", 1, CancellationToken);

        var admitted = await repo.AdmitAsync(other.Id, "a", 1, CancellationToken);

        admitted.Should().Be(ScopeIdDerivation.Derive(other.Id, "a"));
    }

    [TestMethod]
    public async Task RecordActivityAsync_RepeatedCalls_BumpCountersAndKeepLastSeenForwardOnly()
    {
        var services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, agent.Project.Id, "support");
        var newer = DateTimeOffset.UtcNow;
        var older = newer.AddMinutes(-5);

        await repo.RecordActivityAsync(scopeId, agent.CurrentVersion.Id, 50, newer, CancellationToken);
        await repo.RecordActivityAsync(scopeId, agent.CurrentVersion.Id, 70, older, CancellationToken);

        var overview = await repo.GetOverviewAsync(scopeId, CancellationToken);
        ArgumentNullException.ThrowIfNull(overview);
        overview.TraceCount.Should().Be(2);
        overview.TotalTokens.Should().Be(120);
        overview.LastActivityAt.Should().Be(newer, "an out-of-order ingest must not rewind activity");
        overview.Agents.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ScopeAgentStat(agent.Id, 2, 120, newer, newer));
    }

    [TestMethod]
    public async Task GetOverviewsAsync_AgentsInSeveralScopes_ReportsMembershipPerScope()
    {
        var services = GetServices();
        var agents = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var shared = await agents.CreateAsync(CancellationToken);
        var billingOnly = await agents.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var projectId = shared.Project.Id;
        var support = await AdmitAsync(repo, projectId, "support");
        var billing = await AdmitAsync(repo, projectId, "billing");
        var now = DateTimeOffset.UtcNow;

        await repo.RecordActivityAsync(support, shared.CurrentVersion.Id, 10, now, CancellationToken);
        await repo.RecordActivityAsync(billing, shared.CurrentVersion.Id, 20, now, CancellationToken);
        await repo.RecordActivityAsync(billing, billingOnly.CurrentVersion.Id, 30, now, CancellationToken);

        var overviews = (await repo.GetOverviewsAsync(projectId, CancellationToken)).ToDictionary(o => o.Scope.Id);
        overviews[support].Agents.Select(a => a.AgentId).Should().BeEquivalentTo([shared.Id]);
        overviews[billing].Agents.Select(a => a.AgentId).Should().BeEquivalentTo([shared.Id, billingOnly.Id]);
        overviews[billing].TotalTokens.Should().Be(50);
        (await repo.GetAgentIdsAsync(billing, CancellationToken)).Should().BeEquivalentTo([shared.Id, billingOnly.Id]);
    }

    [TestMethod]
    public async Task GetOverviewsAsync_ScopeWithoutActivity_IsListedWithZeroCounters()
    {
        var services = GetServices();
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        await AdmitAsync(repo, project.Id, "dormant");

        var overview = (await repo.GetOverviewsAsync(project.Id, CancellationToken)).Should().ContainSingle().Subject;

        overview.TraceCount.Should().Be(0);
        overview.LastActivityAt.Should().BeNull();
        overview.Agents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetOverviewAsync_AfterVersionMovedToAnotherAgent_AttributesMembershipToNewAgent()
    {
        // Memberships are keyed by version precisely so they follow a version move.
        var services = GetServices();
        var agents = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var source = await agents.CreateAsync(CancellationToken);
        var target = await agents.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, source.Project.Id, "support");
        await repo.RecordActivityAsync(scopeId, source.CurrentVersion.Id, 10, DateTimeOffset.UtcNow, CancellationToken);
        await repo.RecordActivityAsync(scopeId, target.CurrentVersion.Id, 5, DateTimeOffset.UtcNow, CancellationToken);

        await source.CurrentVersion.MoveToAgentAsync(target, CancellationToken);

        var overview = await repo.GetOverviewAsync(scopeId, CancellationToken);
        ArgumentNullException.ThrowIfNull(overview);
        var agent = overview.Agents.Should().ContainSingle().Subject;
        agent.AgentId.Should().Be(target.Id);
        agent.TraceCount.Should().Be(2);
        agent.TotalTokens.Should().Be(15);
    }

    [TestMethod]
    public async Task RecordTraceRemovalsAsync_OvershootingDelta_ClampsAtZero()
    {
        var services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, agent.Project.Id, "support");
        await repo.RecordActivityAsync(scopeId, agent.CurrentVersion.Id, 40, DateTimeOffset.UtcNow, CancellationToken);
        await repo.RecordActivityAsync(scopeId, agent.CurrentVersion.Id, 60, DateTimeOffset.UtcNow, CancellationToken);

        await repo.RecordTraceRemovalsAsync([new ScopeTraceRemoval(scopeId, agent.CurrentVersion.Id, 1, 40)], CancellationToken);
        var afterOne = await repo.GetOverviewAsync(scopeId, CancellationToken);
        await repo.RecordTraceRemovalsAsync([new ScopeTraceRemoval(scopeId, agent.CurrentVersion.Id, 5, 500)], CancellationToken);
        var afterOvershoot = await repo.GetOverviewAsync(scopeId, CancellationToken);

        ArgumentNullException.ThrowIfNull(afterOne);
        ArgumentNullException.ThrowIfNull(afterOvershoot);
        afterOne.TraceCount.Should().Be(1);
        afterOne.TotalTokens.Should().Be(60);
        afterOvershoot.TraceCount.Should().Be(0);
        afterOvershoot.TotalTokens.Should().Be(0);
    }

    [TestMethod]
    public async Task RemoveMembershipsOlderThanAsync_RemovesStaleMembershipsButKeepsScope()
    {
        var services = GetServices();
        var agents = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var stale = await agents.CreateAsync(CancellationToken);
        var fresh = await agents.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, stale.Project.Id, "support");
        var now = DateTimeOffset.UtcNow;
        await repo.RecordActivityAsync(scopeId, stale.CurrentVersion.Id, 10, now.AddDays(-40), CancellationToken);
        await repo.RecordActivityAsync(scopeId, fresh.CurrentVersion.Id, 10, now, CancellationToken);

        var removed = await repo.RemoveMembershipsOlderThanAsync(now.AddDays(-30), CancellationToken);

        removed.Should().Be(1);
        var overview = await repo.GetOverviewAsync(scopeId, CancellationToken);
        ArgumentNullException.ThrowIfNull(overview);
        overview.Agents.Select(a => a.AgentId).Should().BeEquivalentTo([fresh.Id]);
    }

    [TestMethod]
    public async Task GetOverviewAsync_UnknownScope_ReturnsNull()
    {
        var services = GetServices();
        var repo = services.GetRequiredService<IScopeRepository>();

        (await repo.GetOverviewAsync(Guid.NewGuid(), CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task GetScopeRemovalsOlderThanAsync_GroupsTheDoomedCallsByScopeAndVersion()
    {
        var services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var calls = services.GetRequiredService<IAgentCallRepository>();
        var scopeA = Guid.NewGuid();
        var scopeB = Guid.NewGuid();
        await SeedCallAsync(services, agent, scopeA, tokens: 15);
        await SeedCallAsync(services, agent, scopeA, tokens: 10);
        await SeedCallAsync(services, agent, scopeB, tokens: 2);
        await SeedCallAsync(services, agent, scopeId: null, tokens: 200);

        var removals = await calls.GetScopeRemovalsOlderThanAsync(DateTimeOffset.UtcNow.AddDays(1), CancellationToken);

        removals.Should().BeEquivalentTo([
            new ScopeTraceRemoval(scopeA, agent.CurrentVersion.Id, 2, 25),
            new ScopeTraceRemoval(scopeB, agent.CurrentVersion.Id, 1, 2),
        ]);
    }

    private async Task<Guid> AdmitAsync(IScopeRepository repo, Guid projectId, string key)
    {
        var scopeId = await repo.AdmitAsync(projectId, key, 100, CancellationToken);
        ArgumentNullException.ThrowIfNull(scopeId);
        return scopeId.Value;
    }

    private async Task SeedCallAsync(IServiceProvider services, IAgent agent, Guid? scopeId, ulong tokens)
    {
        var request = await services.GetRequiredService<IDomainObjectGenerator<Conversation>>().CreateAsync(CancellationToken);
        ICompletion response = services.GetRequiredService<ICompletion.Create>()(
            new AssistantMessage([Content.FromText("ok")], []), new TokenUsage(tokens, 0), TimeSpan.FromMilliseconds(50));

        IAgentCall call = services.GetRequiredService<IAgentCall.CreateNew>()(
            agent,
            agent.CurrentVersion,
            agent.Endpoint,
            request,
            response,
            httpStatus: HttpStatusCode.OK,
            modelParameters: agent.ModelParameters,
            scopeId: scopeId);

        await services.GetRequiredService<IAgentCallRepository>().AddAsync(call, CancellationToken);
    }
}
