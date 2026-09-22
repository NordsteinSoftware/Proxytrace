using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Proxytrace.Api.Controllers;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Tests;

/// <summary>
/// The settings surface (projects, providers, search indexing) is admin-only. These tests pin the
/// authorization contract at the source: every settings-mutating endpoint must declare
/// <c>[Authorize(Roles = nameof(UserRole.Admin))]</c>, while the handful of reads that back
/// non-admin flows (sidebar project switcher, agent/run/playground endpoint pickers, unified search)
/// must stay reachable by a plain authenticated Member. Enforcement itself is ASP.NET Core's job;
/// this guards against someone silently dropping (or over-applying) the attribute.
/// </summary>
[TestClass]
public sealed class SettingsAuthorizationTests
{
    [TestMethod]
    public void ProjectsController_WriteEndpoints_RequireAdmin()
    {
        AssertRequiresAdmin<ProjectsController>(
            nameof(ProjectsController.Create),
            nameof(ProjectsController.Update),
            nameof(ProjectsController.UpdateDefaultUpstreamProvider),
            nameof(ProjectsController.Delete),
            nameof(ProjectsController.AddMember),
            nameof(ProjectsController.RemoveMember));
    }

    [TestMethod]
    public void ProjectsController_SharedReads_StayOpenToMembers()
    {
        // GetAll backs the sidebar project switcher (ProjectProvider) for every user.
        AssertDoesNotRequireAdmin<ProjectsController>(
            nameof(ProjectsController.GetAll),
            nameof(ProjectsController.Get),
            nameof(ProjectsController.GetMembers));
    }

    [TestMethod]
    public void SearchController_SettingsEndpoints_RequireAdmin()
    {
        AssertRequiresAdmin<SearchController>(
            nameof(SearchController.Reindex),
            nameof(SearchController.GetSettings),
            nameof(SearchController.UpdateSettings),
            nameof(SearchController.GetStatus));
    }

    [TestMethod]
    public void SearchController_QueryEndpoints_StayOpenToMembers()
    {
        // The unified search box in the top bar is used by everyone.
        AssertDoesNotRequireAdmin<SearchController>(
            nameof(SearchController.Search),
            nameof(SearchController.Recent));
    }

    [TestMethod]
    public void ModelProvidersController_ManagementEndpoints_RequireAdmin()
    {
        AssertRequiresAdmin<ModelProvidersController>(
            nameof(ModelProvidersController.GetAll),
            nameof(ModelProvidersController.GetOverview),
            nameof(ModelProvidersController.Create),
            nameof(ModelProvidersController.Update),
            nameof(ModelProvidersController.Delete),
            nameof(ModelProvidersController.Reload),
            nameof(ModelProvidersController.GetAvailableModels),
            nameof(ModelProvidersController.GetModels),
            nameof(ModelProvidersController.CreateModel),
            nameof(ModelProvidersController.DeleteModel),
            nameof(ModelProvidersController.UpdateModelPricing),
            nameof(ModelProvidersController.GetKeys),
            nameof(ModelProvidersController.CreateKey),
            nameof(ModelProvidersController.DeleteKey));
    }

    [TestMethod]
    public void ModelProvidersController_SharedReads_StayOpenToMembers()
    {
        // GetAllModelEndpoints backs agent endpoint selection, test runs, and the playground.
        // Get (provider by id) backs the Tracey assistant's provider-detail tool.
        AssertDoesNotRequireAdmin<ModelProvidersController>(
            nameof(ModelProvidersController.GetAllModelEndpoints),
            nameof(ModelProvidersController.Get));
    }

    [TestMethod]
    public void UsersController_RosterEndpoints_RequireAdmin()
    {
        // The full roster, any user's record, and any user's project memberships expose other
        // users' emails (PII) and role assignments — admin-only, like the mutating endpoints.
        AssertRequiresAdmin<UsersController>(
            nameof(UsersController.GetAll),
            nameof(UsersController.Get),
            nameof(UsersController.GetProjects),
            nameof(UsersController.UpdateRole),
            nameof(UsersController.Delete));
    }

    [TestMethod]
    public void UsersController_SelfServiceEndpoints_StayOpenToMembers()
    {
        // The account menu (own profile, own language, own email-notification prefs) is used by
        // every authenticated user and must not require Admin.
        AssertDoesNotRequireAdmin<UsersController>(
            nameof(UsersController.Me),
            nameof(UsersController.UpdateMyLanguage),
            nameof(UsersController.UpdateMyEmailNotifications));
    }

    private static void AssertRequiresAdmin<TController>(params string[] methodNames)
    {
        foreach (var name in methodNames)
        {
            RequiresAdmin(MethodOf<TController>(name))
                .Should().BeTrue($"{typeof(TController).Name}.{name} is a settings-mutating endpoint and must require the Admin role");
        }
    }

    private static void AssertDoesNotRequireAdmin<TController>(params string[] methodNames)
    {
        foreach (var name in methodNames)
        {
            RequiresAdmin(MethodOf<TController>(name))
                .Should().BeFalse($"{typeof(TController).Name}.{name} backs a non-admin flow and must stay reachable by a Member");
        }
    }

    private static bool RequiresAdmin(MethodInfo method) =>
        method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Any(a => a.Roles is not null
                && a.Roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Contains(nameof(UserRole.Admin)));

    private static MethodInfo MethodOf<TController>(string name)
    {
        var method = typeof(TController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
        if (method is null)
            throw new InvalidOperationException($"{typeof(TController).Name} should declare a public method '{name}'");
        return method;
    }
}
