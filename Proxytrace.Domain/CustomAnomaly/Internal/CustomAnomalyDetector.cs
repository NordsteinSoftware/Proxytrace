using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.CustomAnomaly.Internal;

internal record CustomAnomalyDetector : DomainEntity<ICustomAnomalyDetector>, ICustomAnomalyDetector
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; private init; }
    /// <summary>
    /// Gets or sets the agent.
    /// </summary>
    public IAgent Agent { get; private init; }
    /// <summary>
    /// Gets or sets the triggers.
    /// </summary>
    public IReadOnlyList<AnomalyTrigger> Triggers { get; private init; }
    /// <summary>
    /// Gets or sets the all agents.
    /// </summary>
    public bool AllAgents { get; private init; }
    /// <summary>
    /// Gets or sets the scoped agents.
    /// </summary>
    public IReadOnlyCollection<IAgent> ScopedAgents { get; private init; }
    /// <summary>
    /// Gets or sets the is enabled.
    /// </summary>
    public bool IsEnabled { get; private init; }
    /// <summary>
    /// Gets or sets the block upstream.
    /// </summary>
    public bool BlockUpstream { get; private init; }

    /// <summary>
    /// Provides additional functionality.
    /// </summary>
    public IProject Project
        => Agent.Project;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyDetector"/> class.
    /// </summary>
    public CustomAnomalyDetector(
        string name,
        IAgent agent,
        IReadOnlyList<AnomalyTrigger> triggers,
        bool allAgents,
        IReadOnlyCollection<IAgent> scopedAgents,
        bool isEnabled,
        bool blockUpstream,
        IRepository<ICustomAnomalyDetector> repository) : base(repository)
    {
        Name = name;
        Agent = agent;
        Triggers = triggers.ToArray();
        AllAgents = allAgents;
        ScopedAgents = scopedAgents.ToArray();
        IsEnabled = isEnabled;
        BlockUpstream = blockUpstream;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyDetector"/> class.
    /// </summary>
    public CustomAnomalyDetector(
        string name,
        IAgent agent,
        IReadOnlyList<AnomalyTrigger> triggers,
        bool allAgents,
        IReadOnlyCollection<IAgent> scopedAgents,
        bool isEnabled,
        bool blockUpstream,
        IDomainEntityData existing,
        IRepository<ICustomAnomalyDetector> repository) : base(existing, repository)
    {
        Name = name;
        Agent = agent;
        Triggers = triggers.ToArray();
        AllAgents = allAgents;
        ScopedAgents = scopedAgents.ToArray();
        IsEnabled = isEnabled;
        BlockUpstream = blockUpstream;
    }

    /// <summary>
    /// Updates.
    /// </summary>
    public Task<ICustomAnomalyDetector> Update(
        string name,
        IReadOnlyList<AnomalyTrigger> triggers,
        bool allAgents,
        IReadOnlyCollection<IAgent> scopedAgents,
        bool isEnabled,
        bool blockUpstream,
        CancellationToken cancellationToken = default)
        => ApplyAsync(this with
        {
            Name = name,
            Triggers = triggers.ToArray(),
            AllAgents = allAgents,
            ScopedAgents = scopedAgents.ToArray(),
            IsEnabled = isEnabled,
            BlockUpstream = blockUpstream,
        }, cancellationToken);

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotNullOrWhiteSpace(Name);

        if (Triggers.Count is 0 or > ICustomAnomalyDetector.MaxTriggers)
            yield return new ValidationResult(
                $"A detector must have between 1 and {ICustomAnomalyDetector.MaxTriggers} triggers.",
                [nameof(Triggers)]);

        foreach (var trigger in Triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Pattern))
            {
                yield return new ValidationResult(
                    "A trigger pattern cannot be empty.", [nameof(Triggers)]);
                continue;
            }

            if (trigger.Kind == TriggerKind.Regex && CompileError(trigger.Pattern) is { } error)
                yield return new ValidationResult(
                    $"Trigger regex '{trigger.Pattern}' is invalid: {error}", [nameof(Triggers)]);
        }

        if (!AllAgents && ScopedAgents.Count == 0)
            yield return new ValidationResult(
                "A detector that does not apply to all agents must scope at least one agent.",
                [nameof(ScopedAgents)]);

        foreach (var result in Agent.Validate(validationContext))
            yield return result;

        yield return Validation.True(Agent.IsSystemAgent);
    }

    /// <summary>
    /// Validates a regex trigger with the SAME options the review pipeline matches with —
    /// <c>NonBacktracking</c> rejects backreferences/lookarounds at construction, so anything that
    /// passes here is guaranteed to be safely matchable at runtime.
    /// </summary>
    private static string? CompileError(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
            return null;
        }
        // ArgumentException (RegexParseException) for malformed patterns; NotSupportedException for
        // constructs NonBacktracking rejects (backreferences, lookarounds).
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
    }
}
