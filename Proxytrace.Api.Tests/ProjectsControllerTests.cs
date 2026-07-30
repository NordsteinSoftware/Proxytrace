using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Rest;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Projects;
using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class ProjectsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Create_WithMemberIds_ReturnsDtoWithMembers()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateProjectRequest("New project", endpoint.Id, [user.Id]),
            CancellationToken);

        var actionResult = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var created = actionResult.Value as ProjectDto
            ?? throw new InvalidOperationException("Expected ProjectDto value.");
        created.Members.Should().HaveCount(1);
        created.Members.Single().Id.Should().Be(user.Id);
    }

    [TestMethod]
    public async Task AddMember_PersistsAndReturnsUpdatedDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var (project, user) = await SeedProjectAndUserAsync(services);

        var result = await controller.AddMember(project.Id, user.Id, CancellationToken);

        var dto = result.Value ?? throw new InvalidOperationException("Expected non-null Value.");
        dto.Members.Should().ContainSingle(m => m.Id == user.Id);
    }

    [TestMethod]
    public async Task AddMember_Idempotent_NoDuplicate()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var (project, user) = await SeedProjectAndUserAsync(services);

        await controller.AddMember(project.Id, user.Id, CancellationToken);
        var second = await controller.AddMember(project.Id, user.Id, CancellationToken);

        (second.Value ?? throw new InvalidOperationException("Expected non-null Value.")).Members.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task RemoveMember_PersistsAndReturnsUpdatedDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var (project, user) = await SeedProjectAndUserAsync(services);
        await controller.AddMember(project.Id, user.Id, CancellationToken);

        var result = await controller.RemoveMember(project.Id, user.Id, CancellationToken);

        (result.Value ?? throw new InvalidOperationException("Expected non-null Value.")).Members.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AddMember_UnknownUser_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var projectGenerator = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var project = await projectGenerator.CreateAsync(CancellationToken);

        var result = await controller.AddMember(project.Id, Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task AddMember_UnknownProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.AddMember(Guid.NewGuid(), Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Update_RenamesProject_ButLeavesMembershipUnchanged()
    {
        // Membership is not mass-assignable through the generic update — it only changes via the
        // dedicated add/remove-member endpoints.
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var (project, userA) = await SeedProjectAndUserAsync(services);
        await controller.AddMember(project.Id, userA.Id, CancellationToken);

        var update = new UpdateProjectRequest("Renamed", project.SystemEndpoint.Id);
        var result = await controller.Update(project.Id, update, CancellationToken);

        var dto = result.Value ?? throw new InvalidOperationException("Expected non-null Value.");
        dto.Name.Should().Be("Renamed");
        dto.Members.Should().ContainSingle(m => m.Id == userA.Id);
    }

    [TestMethod]
    public async Task GetAll_AsNonAdmin_ReturnsOnlyMemberProjects()
    {
        IServiceProvider services = GetServices();
        var user = await CreateUserAsync(services, UserRole.Member);
        var mine = await ProjectWithMembersAsync(services, user);
        await ProjectWithMembersAsync(services); // someone else's project

        var controller = ResolveController(services, NewGuard(services, user));
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [TestMethod]
    public async Task GetAll_AsAdmin_ReturnsAllProjects()
    {
        IServiceProvider services = GetServices();
        var admin = await CreateUserAsync(services, UserRole.Admin);
        await ProjectWithMembersAsync(services);
        await ProjectWithMembersAsync(services);

        var controller = ResolveController(services, NewGuard(services, admin));
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GetAll_WithApiKeyScopedToOneProject_ReturnsOnlyThatProject()
    {
        // #474: the listing used to be driven by an inline role/membership check that never saw the
        // key's project, so a key minted for A listed every project its (admin) owner could reach.
        IServiceProvider services = GetServices();
        var owner = await CreateUserAsync(services, UserRole.Admin);
        var projectA = await ProjectWithMembersAsync(services);
        var projectB = await ProjectWithMembersAsync(services);

        var controller = ResolveController(services, NewGuard(services, owner, apiKeyProjectId: projectA.Id));
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(projectA.Id);
        result.Total.Should().Be(1);
        result.Items.Should().NotContain(p => p.Id == projectB.Id);
    }

    [TestMethod]
    public async Task Get_AsNonMember_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var outsider = await CreateUserAsync(services, UserRole.Member);
        var project = await ProjectWithMembersAsync(services);

        var controller = ResolveController(services, NewGuard(services, outsider));
        var result = await controller.Get(project.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_WithApiKeyScopedToAnotherProject_ReturnsNotFound()
    {
        // A key minted for A must not read B's detail, even though its admin owner could.
        IServiceProvider services = GetServices();
        var owner = await CreateUserAsync(services, UserRole.Admin);
        var projectA = await ProjectWithMembersAsync(services);
        var projectB = await ProjectWithMembersAsync(services);

        var controller = ResolveController(services, NewGuard(services, owner, apiKeyProjectId: projectA.Id));
        var result = await controller.Get(projectB.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_WithApiKeyScopedToThatProject_ReturnsDto()
    {
        IServiceProvider services = GetServices();
        var owner = await CreateUserAsync(services, UserRole.Admin);
        var projectA = await ProjectWithMembersAsync(services);

        var controller = ResolveController(services, NewGuard(services, owner, apiKeyProjectId: projectA.Id));
        var result = await controller.Get(projectA.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(projectA.Id);
    }

    [TestMethod]
    public async Task GetMembers_AsNonMember_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var outsider = await CreateUserAsync(services, UserRole.Member);
        var project = await ProjectWithMembersAsync(services);

        var controller = ResolveController(services, NewGuard(services, outsider));
        var result = await controller.GetMembers(project.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetMembers_WithApiKeyScopedToAnotherProject_ReturnsNotFound()
    {
        // Member emails are PII: a key minted for A must not enumerate B's members.
        IServiceProvider services = GetServices();
        var owner = await CreateUserAsync(services, UserRole.Admin);
        var member = await CreateUserAsync(services, UserRole.Member);
        var projectA = await ProjectWithMembersAsync(services);
        var projectB = await ProjectWithMembersAsync(services, member);

        var controller = ResolveController(services, NewGuard(services, owner, apiKeyProjectId: projectA.Id));
        var result = await controller.GetMembers(projectB.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetMembers_AsMember_ReturnsMembers()
    {
        IServiceProvider services = GetServices();
        var user = await CreateUserAsync(services, UserRole.Member);
        var project = await ProjectWithMembersAsync(services, user);

        var controller = ResolveController(services, NewGuard(services, user));
        var result = await controller.GetMembers(project.Id, CancellationToken);

        result.Value.Should().ContainSingle(m => m.Id == user.Id);
    }

    [TestMethod]
    public async Task Delete_RemovesBuiltInTraceyAgent_ThenDeletesProject()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        // Give the project its built-in Tracey system agent, exactly as project creation does.
        await services.GetRequiredService<Proxytrace.Application.Tracey.ITraceyAgentProvisioner>()
            .EnsureTraceyAgentAsync(project, CancellationToken);

        var result = await controller.Delete(project.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        (await services.GetRequiredService<IProjectRepository>().FindAsync(project.Id, CancellationToken))
            .Should().BeNull();
    }

    [TestMethod]
    public async Task Delete_ProjectWithUserAgent_ReturnsConflict()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        // A generated agent is a normal (non-system) agent; deleting its project must be refused.
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        agent.IsSystemAgent.Should().BeFalse();

        var result = await controller.Delete(agent.Project.Id, CancellationToken);

        result.Should().BeOfType<ConflictObjectResult>();
        (await services.GetRequiredService<IProjectRepository>().FindAsync(agent.Project.Id, CancellationToken))
            .Should().NotBeNull();
    }

    [TestMethod]
    public async Task Delete_UnknownProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    /// <summary>
    /// Builds the controller. Without an explicit <paramref name="accessGuard"/> the permissive
    /// stub from the test module is used, so tests that do not care about tenant scoping stay
    /// unaffected; the access tests pass a real guard built by <see cref="NewGuard"/>.
    /// </summary>
    private static ProjectsController ResolveController(
        IServiceProvider services,
        IProjectAccessGuard? accessGuard = null) =>
        new(services.GetRequiredService<IProjectRepository>(),
            services.GetRequiredService<IRepository<IModelEndpoint>>(),
            services.GetRequiredService<IRepository<IUser>>(),
            services.GetRequiredService<IAgentRepository>(),
            services.GetRequiredService<IProject.CreateNew>(),
            services.GetRequiredService<IProject.CreateExisting>(),
            services.GetRequiredService<Proxytrace.Application.Tracey.ITraceyAgentProvisioner>(),
            services.GetRequiredService<Proxytrace.Application.Evaluator.IDefaultEvaluatorProvisioner>(),
            accessGuard ?? services.GetRequiredService<IProjectAccessGuard>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);

    /// <summary>
    /// The real guard, wired to a given caller and — optionally — to a REST API key confined to one
    /// project, exactly as <c>ApiKeyAuthenticationHandler</c> marks the request.
    /// </summary>
    private static ProjectAccessGuard NewGuard(
        IServiceProvider services,
        IUser? currentUser,
        Guid? apiKeyProjectId = null)
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(currentUser);

        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (apiKeyProjectId is { } projectId)
            httpContextAccessor.HttpContext.Items[ApiKeyAuthenticationHandler.ProjectIdItemKey] = projectId;

        return new ProjectAccessGuard(
            accessor,
            services.GetRequiredService<IProjectRepository>(),
            httpContextAccessor);
    }

    private async Task<IUser> CreateUserAsync(IServiceProvider services, UserRole role)
    {
        // The IUser generator picks a random role, which would make a role-sensitive test flaky.
        var create = services.GetRequiredService<IUser.CreateNew>();
        var user = create($"{Guid.NewGuid():N}@example.test", externalSubject: null, passwordHash: "hash", role);
        return await services.GetRequiredService<IRepository<IUser>>().AddAsync(user, CancellationToken);
    }

    private async Task<IProject> ProjectWithMembersAsync(IServiceProvider services, params IUser[] members)
    {
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var createNew = services.GetRequiredService<IProject.CreateNew>();
        var project = createNew($"P-{Guid.NewGuid():N}", endpoint, members);
        return await services.GetRequiredService<IProjectRepository>().AddAsync(project, CancellationToken);
    }

    private async Task<(IProject project, IUser user)> SeedProjectAndUserAsync(IServiceProvider services)
    {
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        return (project, user);
    }
}
