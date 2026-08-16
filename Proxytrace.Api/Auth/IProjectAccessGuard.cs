using Proxytrace.Api.Auth.Rest;
using Proxytrace.Application.Auth;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Auth;

/// <summary>
/// Central cross-tenant authorization check. The app is multi-tenant: every resource belongs to a
/// <see cref="IProject"/>, users belong to projects via <c>Project.Members</c>, and the
/// <see cref="UserRole.Admin"/> role bypasses membership. Controllers resolve the owning project id
/// of the resource they are about to read/mutate and ask this guard whether the caller may touch it,
/// rather than trusting a raw route/query id (which is the IDOR fixed in #193).
/// </summary>
public interface IProjectAccessGuard
{
    /// <summary>True if the caller is an admin or a member of <paramref name="projectId"/>.</summary>
    Task<bool> CanAccessProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The set of project ids the caller may see, or <c>null</c> for an admin (who may see all).
    /// Used to scope list endpoints: a non-admin is restricted to their member projects instead of
    /// receiving every tenant's rows when an optional <c>projectId</c> filter is omitted.
    /// </summary>
    Task<IReadOnlyCollection<Guid>?> GetAccessibleProjectIdsAsync(CancellationToken cancellationToken = default);
}

internal sealed class ProjectAccessGuard : IProjectAccessGuard
{
    private readonly ICurrentUserAccessor currentUser;
    private readonly IProjectRepository projects;
    private readonly IHttpContextAccessor httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectAccessGuard"/> class.
    /// </summary>
    public ProjectAccessGuard(
        ICurrentUserAccessor currentUser,
        IProjectRepository projects,
        IHttpContextAccessor httpContextAccessor)
    {
        this.currentUser = currentUser;
        this.projects = projects;
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the caller is an admin or a member of
    /// <paramref name="projectId"/>, and the request's API key (if any) is confined to that project.
    /// </summary>
    public async Task<bool> CanAccessProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // A REST API key is confined to the project it was minted for, on top of whatever its owner
        // may reach. This check must come first: the key acts as its owner, and since minting is an
        // admin-only endpoint the owner is typically an Admin — whose role alone would otherwise
        // satisfy the membership check below for every project in the instance.
        if (ApiKeyProjectId is { } keyProjectId && keyProjectId != projectId)
            return false;

        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return false;
        if (user.Role == UserRole.Admin)
            return true;
        var memberships = await projects.GetByMemberAsync(user.Id, cancellationToken);
        return memberships.Any(p => p.Id == projectId);
    }

    /// <summary>
    /// Returns the set of project ids the caller may see — <see langword="null"/> for an admin who
    /// may see all, an empty collection when the caller may see none, and the caller's member
    /// projects otherwise. A REST API key further narrows the result to its single project.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>?> GetAccessibleProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return [];

        IReadOnlyCollection<Guid>? ownerScope = user.Role == UserRole.Admin
            ? null
            : (await projects.GetByMemberAsync(user.Id, cancellationToken)).Select(p => p.Id).ToArray();

        // Same confinement for list endpoints: an API-key request sees exactly its own project (and
        // only if the owner could reach it anyway), never the unscoped admin "all projects" null.
        if (ApiKeyProjectId is { } keyProjectId)
            return ownerScope is null || ownerScope.Contains(keyProjectId) ? [keyProjectId] : [];

        return ownerScope;
    }

    /// <summary>
    /// The project a REST API key confines this request to, or <see langword="null"/> when the
    /// request was not authenticated with an API key (a JWT/session request is unconfined).
    /// </summary>
    private Guid? ApiKeyProjectId =>
        httpContextAccessor.HttpContext?.Items[ApiKeyAuthenticationHandler.ProjectIdItemKey] as Guid?;
}
