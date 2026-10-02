using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nordstein.Core.Testing;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Scopes;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class ScopesControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_ReturnsProjectScopesWithMemberAgents()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var support = await AdmitAsync(repo, agent.Project.Id, "support");
        await AdmitAsync(repo, agent.Project.Id, "billing");
        await repo.RecordActivityAsync(support, agent.CurrentVersion.Id, 40, DateTimeOffset.UtcNow, CancellationToken);

        var result = await ResolveController(services).GetAll(agent.Project.Id, CancellationToken);

        result.Select(s => s.Key).Should().BeEquivalentTo(["support", "billing"]);
        var scoped = result.Single(s => s.Id == support);
        scoped.AgentIds.Should().Equal(agent.Id);
        scoped.TraceCount.Should().Be(1);
        scoped.TotalTokens.Should().Be(40);
    }

    [TestMethod]
    public async Task GetAll_ForInaccessibleProject_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var projectId = Guid.NewGuid();
        await AdmitAsync(services.GetRequiredService<IScopeRepository>(), projectId, "support");

        var result = await ResolveController(services, DenyingGuard()).GetAll(projectId, CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Get_ExistingScope_ReturnsAgentsWithNames()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, agent.Project.Id, "support");
        await repo.RecordActivityAsync(scopeId, agent.CurrentVersion.Id, 40, DateTimeOffset.UtcNow, CancellationToken);

        var result = await ResolveController(services).Get(scopeId, CancellationToken);

        var dto = result.Value;
        ArgumentNullException.ThrowIfNull(dto);
        dto.Key.Should().Be("support");
        var member = dto.Agents.Should().ContainSingle().Subject;
        member.AgentId.Should().Be(agent.Id);
        member.AgentName.Should().Be(agent.Name);
    }

    [TestMethod]
    public async Task Get_WhenCallerCannotAccessProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var scopeId = await AdmitAsync(services.GetRequiredService<IScopeRepository>(), Guid.NewGuid(), "support");

        var result = await ResolveController(services, DenyingGuard()).Get(scopeId, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();

        var result = await ResolveController(services).Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Update_ChangesDetailsKeepsKeyAndAudits()
    {
        IServiceProvider services = GetServices();
        var projectId = Guid.NewGuid();
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, projectId, "support");
        var audit = new RecordingAuditLogger();

        var result = await ResolveController(services, audit: audit).Update(
            scopeId, new UpdateScopeRequest("Support crew", "Tier-1 triage"), CancellationToken);

        var dto = result.Value;
        ArgumentNullException.ThrowIfNull(dto);
        dto.DisplayName.Should().Be("Support crew");
        dto.Key.Should().Be("support");
        var stored = await repo.GetAsync(scopeId, CancellationToken);
        stored.Description.Should().Be("Tier-1 triage");
        audit.Events.Should().ContainSingle().Which.Id.Should().Be((int)AuditAction.ScopeUpdated);
    }

    [TestMethod]
    public async Task Update_WhenCallerCannotAccessProject_ReturnsNotFoundAndChangesNothing()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, Guid.NewGuid(), "support");

        var result = await ResolveController(services, DenyingGuard()).Update(
            scopeId, new UpdateScopeRequest("Hijacked", null), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
        (await repo.GetAsync(scopeId, CancellationToken)).DisplayName.Should().BeNull();
    }

    [TestMethod]
    public async Task Delete_ExistingScope_RemovesItKeepsItsTracesAndAudits()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, agent.Project.Id, "support");
        await repo.RecordActivityAsync(scopeId, agent.CurrentVersion.Id, 40, DateTimeOffset.UtcNow, CancellationToken);
        var audit = new RecordingAuditLogger();

        var result = await ResolveController(services, audit: audit).Delete(scopeId, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        (await repo.FindAsync(scopeId, CancellationToken)).Should().BeNull();
        (await repo.GetAgentIdsAsync(scopeId, CancellationToken)).Should().BeEmpty();
        audit.Events.Should().ContainSingle().Which.Id.Should().Be((int)AuditAction.ScopeDeleted);
    }

    [TestMethod]
    public async Task Delete_WhenCallerCannotAccessProject_ReturnsNotFoundAndKeepsScope()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, Guid.NewGuid(), "support");

        var result = await ResolveController(services, DenyingGuard()).Delete(scopeId, CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
        (await repo.FindAsync(scopeId, CancellationToken)).Should().NotBeNull();
    }

    [TestMethod]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();

        var result = await ResolveController(services).Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    private async Task<Guid> AdmitAsync(IScopeRepository repo, Guid projectId, string key)
    {
        var scopeId = await repo.AdmitAsync(projectId, key, 100, CancellationToken);
        ArgumentNullException.ThrowIfNull(scopeId);
        return scopeId.Value;
    }

    // A non-admin who is a member of nothing: every project is inaccessible.
    private static IProjectAccessGuard DenyingGuard()
    {
        var guard = Substitute.For<IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([]));
        return guard;
    }

    private static ScopesController ResolveController(
        IServiceProvider services, IProjectAccessGuard? guard = null, ILogger<Audit>? audit = null)
        => new(
            services.GetRequiredService<IScopeRepository>(),
            services.GetRequiredService<IAgentRepository>(),
            guard ?? services.GetRequiredService<IProjectAccessGuard>(),
            audit ?? NullLogger<Audit>.Instance);

    // Captures the EventIds the controller audits (same shape as the helper in
    // ModelProvidersControllerTests).
    private sealed class RecordingAuditLogger : ILogger<Audit>
    {
        public List<EventId> Events { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Events.Add(eventId);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
