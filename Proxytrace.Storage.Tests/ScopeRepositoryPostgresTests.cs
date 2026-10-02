using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Scope;
using Testcontainers.PostgreSql;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Runs the scope repository's relational-only paths against a real PostgreSQL. The in-memory
/// provider the rest of <see cref="ScopeRepositoryTests"/> uses cannot execute raw SQL or
/// <c>ExecuteDelete</c>, so the set-based removal (<c>UPDATE … FROM unnest(…)</c> plus the
/// emptied-membership <c>DELETE</c>), the idle-scope sweep and the membership FK cascade would
/// otherwise only ever run in production. Gated like every container-backed test: skipped without a
/// runtime unless <c>PROXYTRACE_REQUIRE_DOCKER_TESTS</c> is set (see docs/testing.md).
/// </summary>
[TestClass]
public sealed class ScopeRepositoryPostgresTests : BaseTest<Module>
{
    /// <summary>Pinned to the image the deployed stack runs (see <c>docker-compose.yml</c>).</summary>
    private const string PostgresImage = "postgres:16-alpine";

    private static bool DockerRequired
        => Environment.GetEnvironmentVariable("PROXYTRACE_REQUIRE_DOCKER_TESTS") is { } value
           && (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public async Task RecordTraceRemovalsAsync_OnPostgres_MergesClampsAndDropsEmptiedMemberships()
    {
        await using var postgres = await StartPostgresAsync();
        var services = await GetPostgresServicesAsync(postgres);
        var agents = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var kept = await agents.CreateAsync(CancellationToken);
        var emptied = await agents.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, kept.Project.Id, "support");
        var now = DateTimeOffset.UtcNow;
        await repo.RecordActivitiesAsync(scopeId, kept.CurrentVersion.Id, 5, 500, now, now, CancellationToken);
        await repo.RecordActivitiesAsync(scopeId, emptied.CurrentVersion.Id, 1, 10, now, now, CancellationToken);

        await repo.RecordTraceRemovalsAsync(
            [
                // Two deltas for one membership must both apply, not just the one the join picks.
                new ScopeTraceRemoval(scopeId, kept.CurrentVersion.Id, 1, 100),
                new ScopeTraceRemoval(scopeId, kept.CurrentVersion.Id, 2, 200),
                // Overshoots: clamped at zero, which empties the membership.
                new ScopeTraceRemoval(scopeId, emptied.CurrentVersion.Id, 3, 30),
            ],
            CancellationToken);

        var overview = await repo.GetOverviewAsync(scopeId, CancellationToken);
        ArgumentNullException.ThrowIfNull(overview);
        overview.Agents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { AgentId = kept.Id, TraceCount = 2, TotalTokens = 200L });
    }

    [TestMethod]
    public async Task RemoveIdleOlderThanAsync_OnPostgres_SweepsOnlyIdleUncuratedScopes()
    {
        await using var postgres = await StartPostgresAsync();
        var services = await GetPostgresServicesAsync(postgres);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var projectId = agent.Project.Id;
        var idle = await AdmitAsync(repo, projectId, "per-user-1");
        var named = await AdmitAsync(repo, projectId, "named");
        var active = await AdmitAsync(repo, projectId, "active");
        await (await repo.GetAsync(named, CancellationToken)).ChangeDetails("Named", null, CancellationToken);
        await repo.RecordActivityAsync(active, agent.CurrentVersion.Id, 10, DateTimeOffset.UtcNow, CancellationToken);

        var removed = await repo.RemoveIdleOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken);

        removed.Should().Be(1);
        (await repo.FindAsync(idle, CancellationToken)).Should().BeNull();
        (await repo.GetOverviewsAsync(projectId, CancellationToken)).Select(o => o.Scope.Id)
            .Should().BeEquivalentTo([named, active]);
    }

    [TestMethod]
    public async Task RemoveAsync_OnPostgres_CascadesToMemberships()
    {
        await using var postgres = await StartPostgresAsync();
        var services = await GetPostgresServicesAsync(postgres);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IScopeRepository>();
        var scopeId = await AdmitAsync(repo, agent.Project.Id, "support");
        await repo.RecordActivityAsync(scopeId, agent.CurrentVersion.Id, 10, DateTimeOffset.UtcNow, CancellationToken);

        (await repo.RemoveAsync(scopeId, CancellationToken)).Should().BeTrue();

        (await repo.GetAgentIdsAsync(scopeId, CancellationToken)).Should().BeEmpty();
    }

    private async Task<IServiceProvider> GetPostgresServicesAsync(PostgreSqlContainer postgres)
    {
        var services = GetServices(builder =>
            builder.RegisterInstance(StorageConfiguration.Postgres(postgres.GetConnectionString()))
                .As<StorageConfiguration>());
        await services.GetRequiredService<Func<StorageDbContext>>()().Database.MigrateAsync(CancellationToken);
        return services;
    }

    private async Task<Guid> AdmitAsync(IScopeRepository repo, Guid projectId, string key)
    {
        var scopeId = await repo.AdmitAsync(projectId, key, 100, CancellationToken);
        ArgumentNullException.ThrowIfNull(scopeId);
        return scopeId.Value;
    }

    // The guard wraps Build() as well as StartAsync(): Build() pings the Docker endpoint, so that is
    // where a missing runtime throws (#526).
    private async Task<PostgreSqlContainer> StartPostgresAsync()
    {
        PostgreSqlContainer? container = null;
        try
        {
            container = new PostgreSqlBuilder(PostgresImage).Build();
            await container.StartAsync(CancellationToken);
            return container;
        }
        catch (Exception ex) when (!DockerRequired && ex is not OperationCanceledException)
        {
            if (container is not null)
                await container.DisposeAsync();
            Assert.Inconclusive($"Docker is not available, skipping the PostgreSQL-backed test: {ex.Message}");
            throw;
        }
    }
}
