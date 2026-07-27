using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Rest;
using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

/// <summary>
/// Unit tests for the central cross-tenant guard (#193). Admins bypass membership; everyone else is
/// confined to the projects they belong to; an unauthenticated/unknown caller gets nothing.
/// </summary>
[TestClass]
public sealed class ProjectAccessGuardTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CanAccessProject_AsAdmin_AlwaysTrue()
    {
        IServiceProvider services = GetServices();
        var guard = NewGuard(services, await AdminUserAsync(services));

        (await guard.CanAccessProjectAsync(Guid.NewGuid(), CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task CanAccessProject_AsMember_TrueForOwnProject()
    {
        IServiceProvider services = GetServices();
        var member = await MemberUserAsync(services);
        var project = await ProjectWithMembersAsync(services, member);
        var guard = NewGuard(services, member);

        (await guard.CanAccessProjectAsync(project.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task CanAccessProject_AsNonMember_False()
    {
        IServiceProvider services = GetServices();
        var member = await MemberUserAsync(services);
        var otherProject = await ProjectWithMembersAsync(services); // member not added
        var guard = NewGuard(services, member);

        (await guard.CanAccessProjectAsync(otherProject.Id, CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task CanAccessProject_NoCurrentUser_False()
    {
        IServiceProvider services = GetServices();
        var guard = NewGuard(services, currentUser: null);

        (await guard.CanAccessProjectAsync(Guid.NewGuid(), CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task GetAccessibleProjectIds_AsAdmin_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var guard = NewGuard(services, await AdminUserAsync(services));

        (await guard.GetAccessibleProjectIdsAsync(CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task GetAccessibleProjectIds_AsMember_ReturnsOnlyMemberProjects()
    {
        IServiceProvider services = GetServices();
        var member = await MemberUserAsync(services);
        var mine = await ProjectWithMembersAsync(services, member);
        await ProjectWithMembersAsync(services); // someone else's project
        var guard = NewGuard(services, member);

        var ids = await guard.GetAccessibleProjectIdsAsync(CancellationToken);

        ids.Should().NotBeNull();
        ids.Should().ContainSingle().Which.Should().Be(mine.Id);
    }

    [TestMethod]
    public async Task GetAccessibleProjectIds_NoCurrentUser_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var guard = NewGuard(services, currentUser: null);

        (await guard.GetAccessibleProjectIdsAsync(CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task CanAccessProject_WithApiKeyScopedToAnotherProject_FalseEvenForAdminOwner()
    {
        IServiceProvider services = GetServices();
        // The key's owner is an Admin — POST /api/providers/{id}/keys is admin-only, so this is the
        // ordinary case, not a corner one. Role alone must not widen the key beyond its own project.
        var admin = await AdminUserAsync(services);
        var keyProject = await ProjectWithMembersAsync(services);
        var otherProject = await ProjectWithMembersAsync(services);
        var guard = NewGuard(services, admin, apiKeyProjectId: keyProject.Id);

        (await guard.CanAccessProjectAsync(otherProject.Id, CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task CanAccessProject_WithApiKeyScopedToThatProject_True()
    {
        IServiceProvider services = GetServices();
        var admin = await AdminUserAsync(services);
        var keyProject = await ProjectWithMembersAsync(services);
        var guard = NewGuard(services, admin, apiKeyProjectId: keyProject.Id);

        (await guard.CanAccessProjectAsync(keyProject.Id, CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task GetAccessibleProjectIds_WithApiKey_ReturnsOnlyTheKeyProject_NotAdminWildcard()
    {
        IServiceProvider services = GetServices();
        var admin = await AdminUserAsync(services);
        var keyProject = await ProjectWithMembersAsync(services);
        await ProjectWithMembersAsync(services); // another project the admin would otherwise see
        var guard = NewGuard(services, admin, apiKeyProjectId: keyProject.Id);

        var ids = await guard.GetAccessibleProjectIdsAsync(CancellationToken);

        // null would mean "all projects" — the admin wildcard must not survive API-key auth.
        ids.Should().NotBeNull();
        ids.Should().ContainSingle().Which.Should().Be(keyProject.Id);
    }

    [TestMethod]
    public async Task GetAccessibleProjectIds_WithApiKeyOwnerNotAMember_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var member = await MemberUserAsync(services);
        var keyProject = await ProjectWithMembersAsync(services); // member deliberately not added
        var guard = NewGuard(services, member, apiKeyProjectId: keyProject.Id);

        (await guard.GetAccessibleProjectIdsAsync(CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ResolveListScope_AsAdminWithoutProjectFilter_ReturnsNullForAllProjects()
    {
        IServiceProvider services = GetServices();
        var guard = NewGuard(services, await AdminUserAsync(services));

        (await guard.ResolveListScopeAsync(null, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task ResolveListScope_AsMemberWithoutProjectFilter_ReturnsTheirProjects()
    {
        // #482: an unfiltered list from a non-admin used to resolve to "nothing", so every list
        // endpoint answered with an empty page instead of the caller's own rows.
        IServiceProvider services = GetServices();
        var member = await MemberUserAsync(services);
        var mine = await ProjectWithMembersAsync(services, member);
        var alsoMine = await ProjectWithMembersAsync(services, member);
        await ProjectWithMembersAsync(services); // someone else's project

        var guard = NewGuard(services, member);

        var scope = await guard.ResolveListScopeAsync(null, CancellationToken);

        scope.Should().NotBeNull();
        scope.Should().BeEquivalentTo([mine.Id, alsoMine.Id]);
    }

    [TestMethod]
    public async Task ResolveListScope_WithAccessibleProjectFilter_NarrowsToThatProject()
    {
        IServiceProvider services = GetServices();
        var member = await MemberUserAsync(services);
        var mine = await ProjectWithMembersAsync(services, member);
        await ProjectWithMembersAsync(services, member); // a second membership the filter excludes
        var guard = NewGuard(services, member);

        var scope = await guard.ResolveListScopeAsync(mine.Id, CancellationToken);

        scope.Should().ContainSingle().Which.Should().Be(mine.Id);
    }

    [TestMethod]
    public async Task ResolveListScope_WithInaccessibleProjectFilter_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var member = await MemberUserAsync(services);
        await ProjectWithMembersAsync(services, member);
        var theirs = await ProjectWithMembersAsync(services); // member deliberately not added
        var guard = NewGuard(services, member);

        (await guard.ResolveListScopeAsync(theirs.Id, CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ResolveListScope_WithApiKeyAndNoFilter_ReturnsTheKeyProject()
    {
        // The consumer #482 hurt most: a key is confined to one project, so it has no reason to
        // send a projectId — and used to get an empty page for its own project's rows.
        IServiceProvider services = GetServices();
        var admin = await AdminUserAsync(services);
        var keyProject = await ProjectWithMembersAsync(services);
        await ProjectWithMembersAsync(services); // another project the admin would otherwise see
        var guard = NewGuard(services, admin, apiKeyProjectId: keyProject.Id);

        var scope = await guard.ResolveListScopeAsync(null, CancellationToken);

        scope.Should().ContainSingle().Which.Should().Be(keyProject.Id);
    }

    [TestMethod]
    public async Task ResolveListScope_NoCurrentUser_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var guard = NewGuard(services, currentUser: null);

        (await guard.ResolveListScopeAsync(null, CancellationToken)).Should().BeEmpty();
    }

    private static ProjectAccessGuard NewGuard(
        IServiceProvider services,
        IUser? currentUser,
        Guid? apiKeyProjectId = null)
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(currentUser);

        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (apiKeyProjectId is { } projectId)
        {
            httpContextAccessor.HttpContext.Items[ApiKeyAuthenticationHandler.ProjectIdItemKey] = projectId;
        }

        return new ProjectAccessGuard(
            accessor,
            services.GetRequiredService<IProjectRepository>(),
            httpContextAccessor);
    }

    private async Task<IUser> AdminUserAsync(IServiceProvider services) => await CreateUserAsync(services, UserRole.Admin);
    private async Task<IUser> MemberUserAsync(IServiceProvider services) => await CreateUserAsync(services, UserRole.Member);

    private async Task<IUser> CreateUserAsync(IServiceProvider services, UserRole role)
    {
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
}
