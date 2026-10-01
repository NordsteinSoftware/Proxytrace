namespace Proxytrace.Application.Ingestion;

/// <summary>
/// Limits for the auto-created scopes (use-case groups of agents) admitted at ingestion.
/// </summary>
public sealed record ScopeOptions
{
    /// <summary>
    /// The most scopes one project may hold. A call naming an unseen scope beyond it is ingested
    /// unscoped (and a warning logged) — so a client that puts a per-user value into the scope header
    /// cannot grow the scope table, and every scope picker, without bound.
    /// </summary>
    public int MaxScopesPerProject { get; init; } = 200;
}
