namespace Proxytrace.Domain.Scope;

/// <summary>
/// A scope: a named group of agents inside a project that work together on one use case (e.g.
/// "support-agents" in project "translogica"). Auto-created at ingestion the first time a client
/// sends its key — as the <c>/{project}/{scope}/openai/v1</c> path segment or the
/// <c>x-proxytrace-scope</c> header — and stamped onto every trace as <c>AgentCall.ScopeId</c>.
/// Agent membership is derived from those traces (one agent may serve several scopes); the
/// per-member counters are storage-side, so a user editing the display name never contends with
/// ingestion on this row.
/// </summary>
public interface IScope : IDomainEntity<IScope>
{
    const int MaxDisplayNameLength = 100;
    const int MaxDescriptionLength = 1000;

    /// <summary>The canonical key (see <see cref="ScopeKey"/>). Immutable — it is the URL segment.</summary>
    string ExternalKey { get; }

    Guid ProjectId { get; }

    /// <summary>Optional human-friendly label; the UI falls back to <see cref="ExternalKey"/>.</summary>
    string? DisplayName { get; }

    string? Description { get; }

    /// <summary>
    /// Replaces the user-curated display name and description. Blank values clear the field.
    /// </summary>
    Task<IScope> ChangeDetails(
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IScope CreateNew(
        string externalKey,
        Guid projectId,
        string? displayName = null,
        string? description = null);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IScope CreateExisting(
        string externalKey,
        Guid projectId,
        string? displayName,
        string? description,
        IDomainEntityData existing);
}
