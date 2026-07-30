namespace Proxytrace.Api.Auth;

/// <summary>
/// Helpers for reading the list scope produced by
/// <see cref="IProjectAccessGuard.ResolveListScopeAsync"/>.
///
/// The scope follows the same <c>null</c> convention as
/// <see cref="IProjectAccessGuard.GetAccessibleProjectIdsAsync"/>: <c>null</c> means "every
/// project", an empty collection means "no project", and any other value is the exact set the
/// query must be restricted to.
/// </summary>
internal static class ProjectListScope
{
    /// <summary>
    /// Resolves a list endpoint's optional <c>projectId</c> filter into the projects the query must
    /// actually be restricted to: <c>null</c> when the caller may read every project (an admin
    /// listing unfiltered), an empty collection when it may read none, and otherwise the exact set
    /// to filter by — one id when the request named a project it may read, and the caller's whole
    /// membership when it named none.
    ///
    /// This is what list endpoints must use. Reading
    /// <see cref="IProjectAccessGuard.GetAccessibleProjectIdsAsync"/> directly and *demanding* a
    /// <c>projectId</c> is what produced #482: an unfiltered request from a non-admin — every REST
    /// API key, since a key is confined to a single project and so has no reason to send one — was
    /// answered with an empty page instead of that caller's own rows.
    ///
    /// Derived behaviour, so it is an extension rather than an interface member: there is exactly
    /// one implementation, and it stays correct for every <see cref="IProjectAccessGuard"/>.
    /// </summary>
    public static async Task<IReadOnlyCollection<Guid>?> ResolveListScopeAsync(
        this IProjectAccessGuard guard,
        Guid? requestedProjectId,
        CancellationToken cancellationToken = default)
    {
        var accessible = await guard.GetAccessibleProjectIdsAsync(cancellationToken);

        // No filter: the caller's own reach *is* the scope — every project for an admin (null),
        // their memberships otherwise.
        if (requestedProjectId is not { } projectId)
            return accessible;

        // A named project narrows the scope to itself, provided the caller may read it.
        return accessible is null || accessible.Contains(projectId) ? [projectId] : [];
    }

    /// <summary>
    /// True when the caller may read nothing, so the endpoint can answer with an empty result
    /// without querying at all.
    /// </summary>
    public static bool IsEmpty(this IReadOnlyCollection<Guid>? scope) => scope is { Count: 0 };

    /// <summary>
    /// The single project this scope names, or <c>null</c> when it is unrestricted or spans
    /// several. Lets an endpoint keep using its existing indexed by-one-project query for the
    /// common case — a named project, or a REST API key, which is confined to exactly one.
    /// </summary>
    public static Guid? SingleProject(this IReadOnlyCollection<Guid>? scope) =>
        scope is { Count: 1 } ids ? ids.First() : null;

    /// <summary>
    /// True when the scope admits <paramref name="projectId"/> — either because it is
    /// unrestricted or because it contains that project.
    /// </summary>
    public static bool Admits(this IReadOnlyCollection<Guid>? scope, Guid projectId) =>
        scope is null || scope.Contains(projectId);

    /// <summary>
    /// Splits a scope into the pair <c>AgentCallFilter</c> takes. A scope naming exactly one
    /// project goes through the filter's existing single-project branch, so the hot traces query
    /// keeps its equality predicate against <c>AgentVersion(Project)</c>; only a genuinely
    /// multi-project scope takes the set branch.
    /// </summary>
    public static (Guid? ProjectId, IReadOnlyCollection<Guid>? ProjectIds) ToFilterScope(
        this IReadOnlyCollection<Guid>? scope) =>
        scope.SingleProject() is { } single ? (single, null) : (null, scope);
}
