using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AuditLogRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetPagedNewestFirst_WithProjectScope_ExcludesGlobalAndOtherProjects()
    {
        var services = GetServices();
        var repository = services.GetRequiredService<IAuditLogRepository>();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        await SeedAsync(services, projectA);
        await SeedAsync(services, projectB);
        await SeedAsync(services, projectId: null);

        var paged = await repository.GetPagedNewestFirstAsync(
            1, 50, action: null, actorSearch: null, projectIds: [projectA], includeGlobal: false,
            targetType: null, targetId: null, from: null, to: null, CancellationToken);

        paged.Total.Should().Be(1);
        paged.Items.Should().OnlyContain(e => e.ProjectId == projectA);
    }

    [TestMethod]
    public async Task GetPagedNewestFirst_WithIncludeGlobal_ReturnsProjectAndGlobalRows()
    {
        var services = GetServices();
        var repository = services.GetRequiredService<IAuditLogRepository>();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        await SeedAsync(services, projectA);
        await SeedAsync(services, projectB);
        await SeedAsync(services, projectId: null);

        var paged = await repository.GetPagedNewestFirstAsync(
            1, 50, action: null, actorSearch: null, projectIds: [projectA], includeGlobal: true,
            targetType: null, targetId: null, from: null, to: null, CancellationToken);

        paged.Total.Should().Be(2);
        paged.Items.Should().OnlyContain(e => e.ProjectId == projectA || e.ProjectId == null);
    }

    [TestMethod]
    public async Task GetPagedNewestFirst_WithNullProjectIds_ReturnsEverythingIncludingGlobal()
    {
        var services = GetServices();
        var repository = services.GetRequiredService<IAuditLogRepository>();

        await SeedAsync(services, Guid.NewGuid());
        await SeedAsync(services, Guid.NewGuid());
        await SeedAsync(services, projectId: null);

        var paged = await repository.GetPagedNewestFirstAsync(
            1, 50, action: null, actorSearch: null, projectIds: null, includeGlobal: true,
            targetType: null, targetId: null, from: null, to: null, CancellationToken);

        paged.Total.Should().Be(3);
    }

    [TestMethod]
    public async Task GetPagedNewestFirst_WithActionFilter_ReturnsOnlyMatchingAction()
    {
        var services = GetServices();
        var repository = services.GetRequiredService<IAuditLogRepository>();

        await SeedAsync(services, Guid.NewGuid(), AuditAction.ApiKeyMinted);
        await SeedAsync(services, Guid.NewGuid(), AuditAction.ProjectDeleted);

        var paged = await repository.GetPagedNewestFirstAsync(
            1, 50, action: AuditAction.ApiKeyMinted, actorSearch: null, projectIds: null, includeGlobal: true,
            targetType: null, targetId: null, from: null, to: null, CancellationToken);

        paged.Total.Should().Be(1);
        paged.Items.Should().OnlyContain(e => e.Action == AuditAction.ApiKeyMinted);
    }

    [TestMethod]
    public async Task GetPagedNewestFirst_WithActorSearch_MatchesEmailInfixCaseInsensitively()
    {
        var services = GetServices();
        var repository = services.GetRequiredService<IAuditLogRepository>();

        await SeedAsync(services, Guid.NewGuid(), email: "alice@example.com");
        await SeedAsync(services, Guid.NewGuid(), email: "bob@example.com");

        var paged = await repository.GetPagedNewestFirstAsync(
            1, 50, action: null, actorSearch: "ALICE", projectIds: null, includeGlobal: true,
            targetType: null, targetId: null, from: null, to: null, CancellationToken);

        paged.Total.Should().Be(1);
        paged.Items.Should().ContainSingle(e => e.ActorEmail == "alice@example.com");
    }

    [TestMethod]
    public async Task GetPagedNewestFirst_WithWildcardInActorSearch_MatchesItLiterally()
    {
        // % and _ are LIKE operators, so an unescaped search term made "%" match every actor and
        // "a_b" match "axb". The pattern builder escapes them, so they are matched as typed.
        var services = GetServices();
        var repository = services.GetRequiredService<IAuditLogRepository>();

        await SeedAsync(services, Guid.NewGuid(), email: "alice@example.com");
        await SeedAsync(services, Guid.NewGuid(), email: "bob@example.com");

        var paged = await repository.GetPagedNewestFirstAsync(
            1, 50, action: null, actorSearch: "%", projectIds: null, includeGlobal: true,
            targetType: null, targetId: null, from: null, to: null, CancellationToken);

        paged.Total.Should().Be(0, "no actor email contains a literal percent sign");
    }

    private async Task<IAuditLogEntry> SeedAsync(
        IServiceProvider services,
        Guid? projectId,
        AuditAction action = AuditAction.TestRunStarted,
        string? email = "actor@example.com")
    {
        var create = services.GetRequiredService<IAuditLogEntry.CreateNew>();
        var entry = create(action, AuditActorType.User, Guid.NewGuid(), email, null, projectId,
            "TestRunGroup", Guid.NewGuid(), "label", null, AuditOutcome.Success);
        return await entry.AddAsync(CancellationToken);
    }
}
