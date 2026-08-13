using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.AuditLog;
using Proxytrace.Application.Auth;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class AuditLogControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_AsAdmin_ReturnsEveryEntryIncludingGlobal()
    {
        var services = GetServices();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        await SeedEntryAsync(services, projectA);
        await SeedEntryAsync(services, projectB);
        await SeedEntryAsync(services, projectId: null); // global

        var controller = BuildController(services, Guid.NewGuid(), isAdmin: true, memberProjectIds: []);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value!.Total.Should().Be(3);
    }

    [TestMethod]
    public async Task GetAll_AsMember_ReturnsOnlyOwnProjectEntries_AndNoGlobal()
    {
        var services = GetServices();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        await SeedEntryAsync(services, projectA);
        await SeedEntryAsync(services, projectB);
        await SeedEntryAsync(services, projectId: null); // global

        var controller = BuildController(services, Guid.NewGuid(), isAdmin: false, memberProjectIds: [projectA]);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value!.Total.Should().Be(1);
        result.Value.Items.Should().OnlyContain(e => e.ProjectId == projectA);
    }

    [TestMethod]
    public async Task GetAll_AsMember_FilteringByNonMemberProject_ReturnsEmpty()
    {
        var services = GetServices();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        await SeedEntryAsync(services, projectB);

        var controller = BuildController(services, Guid.NewGuid(), isAdmin: false, memberProjectIds: [projectA]);

        var result = await controller.GetAll(projectId: projectB, cancellationToken: CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value!.Total.Should().Be(0);
    }

    [TestMethod]
    public async Task Get_AsMember_OutOfScopeEntry_ReturnsNotFound()
    {
        var services = GetServices();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var entryB = await SeedEntryAsync(services, projectB);

        var controller = BuildController(services, Guid.NewGuid(), isAdmin: false, memberProjectIds: [projectA]);

        var result = await controller.Get(entryB.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_AsMember_GlobalEntry_ReturnsNotFound()
    {
        // The higher-risk leak path: a member must never retrieve an instance-wide (null-project)
        // row by id, even one they could not see in the list. CanViewAsync short-circuits global rows.
        var services = GetServices();
        var globalEntry = await SeedEntryAsync(services, projectId: null);

        var controller = BuildController(services, Guid.NewGuid(), isAdmin: false, memberProjectIds: [Guid.NewGuid()]);

        var result = await controller.Get(globalEntry.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_AsAdmin_GlobalEntry_ReturnsEntry()
    {
        var services = GetServices();
        var globalEntry = await SeedEntryAsync(services, projectId: null);

        var controller = BuildController(services, Guid.NewGuid(), isAdmin: true, memberProjectIds: []);

        var result = await controller.Get(globalEntry.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(globalEntry.Id);
    }

    [TestMethod]
    public async Task Get_AsMember_OwnProjectEntry_ReturnsEntry()
    {
        var services = GetServices();
        var projectA = Guid.NewGuid();
        var entryA = await SeedEntryAsync(services, projectA);

        var controller = BuildController(services, Guid.NewGuid(), isAdmin: false, memberProjectIds: [projectA]);

        var result = await controller.Get(entryA.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(entryA.Id);
    }

    private async Task<IAuditLogEntry> SeedEntryAsync(IServiceProvider services, Guid? projectId)
    {
        var create = services.GetRequiredService<IAuditLogEntry.CreateNew>();
        var repository = services.GetRequiredService<IAuditLogRepository>();
        var entry = create(
            AuditAction.TestRunStarted,
            AuditActorType.User,
            Guid.NewGuid(),
            "actor@example.com",
            null,
            projectId,
            "TestRunGroup",
            Guid.NewGuid(),
            "label",
            null,
            AuditOutcome.Success);
        return await repository.AddAsync(entry, CancellationToken);
    }

    private static AuditLogController BuildController(
        IServiceProvider services,
        Guid userId,
        bool isAdmin,
        IReadOnlyList<Guid> memberProjectIds)
    {
        var user = Substitute.For<IUser>();
        user.Id.Returns(userId);

        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(user);

        var projects = Substitute.For<IProjectRepository>();
        var memberProjects = memberProjectIds.Select(id =>
        {
            var p = Substitute.For<IProject>();
            p.Id.Returns(id);
            return p;
        }).ToArray();
        projects.GetByMemberAsync(userId, Arg.Any<CancellationToken>()).Returns(memberProjects);

        var controller = new AuditLogController(
            services.GetRequiredService<IAuditLogRepository>(),
            currentUser,
            projects);

        var claims = isAdmin ? new[] { new Claim(ClaimTypes.Role, nameof(UserRole.Admin)) } : [];
        var identity = new ClaimsIdentity(claims, "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }
}
