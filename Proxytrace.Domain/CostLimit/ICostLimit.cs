using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.CostLimit;

/// <summary>
/// A monthly spend budget for a project, or — when <see cref="Agent"/> is set — for a single agent
/// within that project. Spend is the derived LLM cost of the calendar month in UTC; it is never
/// persisted per call, so a price change reprices history and the budget follows.
/// </summary>
/// <remarks>
/// Both thresholds are optional and independent: crossing <see cref="SoftLimitEur"/> raises a
/// warning notification, crossing <see cref="HardLimitEur"/> raises a critical notification and
/// makes the proxy reject further calls for the rest of the month. An agent's spend also counts
/// toward its project's limit.
/// </remarks>
public interface ICostLimit : IDomainEntity<ICostLimit>
{
    /// <summary>The project whose month-to-date spend is measured.</summary>
    IProject Project { get; }

    /// <summary>The agent this limit is scoped to, or <c>null</c> for a project-wide limit.</summary>
    IAgent? Agent { get; }

    /// <summary>The EUR amount at which a warning is raised, or <c>null</c> when unset.</summary>
    decimal? SoftLimitEur { get; }

    /// <summary>The EUR amount at which calls are blocked for the rest of the month, or <c>null</c>.</summary>
    decimal? HardLimitEur { get; }

    /// <summary>Whether the guard evaluates this limit and the proxy enforces its hard threshold.</summary>
    bool Enabled { get; }

    public delegate ICostLimit CreateNew(
        IProject project,
        IAgent? agent,
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled);

    public delegate ICostLimit CreateExisting(
        IProject project,
        IAgent? agent,
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled,
        IDomainEntityData existing);

    /// <summary>
    /// Returns an updated copy with new thresholds. The scope (project/agent) is immutable — a
    /// limit that should apply elsewhere is a different limit.
    /// </summary>
    Task<ICostLimit> Update(
        decimal? softLimitEur,
        decimal? hardLimitEur,
        bool enabled,
        CancellationToken cancellationToken = default);
}
