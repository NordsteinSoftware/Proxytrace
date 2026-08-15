using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.CostLimit;

/// <summary>
/// A monthly spend budget for a project, or — when <see cref="Agent"/> or <see cref="ApiKey"/> is
/// set — for a single agent or a single inbound API key within that project. Spend is the derived
/// LLM cost of the calendar month in UTC; it is never persisted per call, so a price change
/// reprices history and the budget follows.
/// </summary>
/// <remarks>
/// <para>
/// Both thresholds are optional and independent: crossing <see cref="SoftLimitEur"/> raises a
/// warning notification, crossing <see cref="HardLimitEur"/> raises a critical notification and
/// makes the proxy reject further calls for the rest of the month. An agent's and a key's spend
/// also count toward their project's limit.
/// </para>
/// <para>
/// The scope is exactly one of the three: project-wide (<see cref="Agent"/> and
/// <see cref="ApiKey"/> both <c>null</c>), agent, or key. Agent and key are deliberately not
/// combinable — "agent X via key Y" is a cross-product nobody has asked for, and allowing it would
/// make the uniqueness guarantee and the proxy's scope matching considerably harder to reason about.
/// </para>
/// </remarks>
public interface ICostLimit : IDomainEntity<ICostLimit>
{
    /// <summary>The project whose month-to-date spend is measured.</summary>
    IProject Project { get; }

    /// <summary>The agent this limit is scoped to, or <c>null</c> when it is not agent-scoped.</summary>
    IAgent? Agent { get; }

    /// <summary>
    /// The inbound API key this limit is scoped to, or <c>null</c> when it is not key-scoped.
    /// </summary>
    /// <remarks>
    /// Unlike agent scope — which can only match traffic sending <c>x-proxytrace-agent</c> — every
    /// proxied request authenticates with a key, so a key-scoped hard block cannot be evaded by
    /// omitting a header. The exception is the upstream-key authentication path, where the caller
    /// presents the provider's own key and no <see cref="IApiKey"/> exists to attribute to; that
    /// traffic is caught by the project-wide limit alone.
    /// </remarks>
    IApiKey? ApiKey { get; }

    /// <summary>The EUR amount at which a warning is raised, or <c>null</c> when unset.</summary>
    decimal? SoftLimitEur { get; }

    /// <summary>The EUR amount at which calls are blocked for the rest of the month, or <c>null</c>.</summary>
    decimal? HardLimitEur { get; }

    /// <summary>Whether the guard evaluates this limit and the proxy enforces its hard threshold.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate ICostLimit CreateNew(
        IProject project,
        IAgent? agent,
        IApiKey? apiKey,
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate ICostLimit CreateExisting(
        IProject project,
        IAgent? agent,
        IApiKey? apiKey,
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled,
        IDomainEntityData existing);

    /// <summary>
    /// Returns an updated copy with new thresholds. The scope (project/agent/key) is immutable — a
    /// limit that should apply elsewhere is a different limit.
    /// </summary>
    Task<ICostLimit> Update(
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled,
        CancellationToken cancellationToken = default);
}
