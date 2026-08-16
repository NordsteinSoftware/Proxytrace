using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunSchedule.Internal;

internal record TestRunSchedule : DomainEntity<ITestRunSchedule>, ITestRunSchedule
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; private init; }
    /// <summary>
    /// Gets or sets the suite.
    /// </summary>
    public ITestSuite Suite { get; private init; }
    /// <summary>
    /// Gets or sets the endpoints.
    /// </summary>
    public IReadOnlyCollection<IModelEndpoint> Endpoints { get; private init; }
    /// <summary>
    /// Gets or sets the interval.
    /// </summary>
    public TimeSpan Interval { get; private init; }
    /// <summary>
    /// Gets or sets the is enabled.
    /// </summary>
    public bool IsEnabled { get; private init; }
    /// <summary>
    /// Gets or sets the anchor at.
    /// </summary>
    public DateTimeOffset AnchorAt { get; private init; }
    /// <summary>
    /// Gets or sets the next run at.
    /// </summary>
    public DateTimeOffset NextRunAt { get; private init; }
    /// <summary>
    /// Gets or sets the last run at.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunSchedule"/> class.
    /// </summary>
    public TestRunSchedule(
        string name, ITestSuite suite, IReadOnlyCollection<IModelEndpoint> endpoints,
        TimeSpan interval, bool isEnabled, DateTimeOffset anchorAt,
        IRepository<ITestRunSchedule> repository) : base(repository)
    {
        Name = name;
        Suite = suite;
        Endpoints = endpoints.ToArray();
        Interval = interval;
        IsEnabled = isEnabled;
        AnchorAt = anchorAt;
        // First fire is the earliest anchor-aligned instant strictly after creation.
        NextRunAt = AlignForward(anchorAt, interval, CreatedAt);
        LastRunAt = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunSchedule"/> class.
    /// </summary>
    public TestRunSchedule(
        string name, ITestSuite suite, IReadOnlyCollection<IModelEndpoint> endpoints,
        TimeSpan interval, bool isEnabled, DateTimeOffset anchorAt, DateTimeOffset nextRunAt,
        DateTimeOffset? lastRunAt, IDomainEntityData existing, IRepository<ITestRunSchedule> repository)
        : base(existing, repository)
    {
        Name = name;
        Suite = suite;
        Endpoints = endpoints.ToArray();
        Interval = interval;
        IsEnabled = isEnabled;
        AnchorAt = anchorAt;
        NextRunAt = nextRunAt;
        LastRunAt = lastRunAt;
    }

    /// <summary>
    /// The earliest anchor-aligned instant (<c>anchor + k·interval</c>, k ≥ 0) strictly after
    /// <paramref name="after"/>. Returns <paramref name="anchor"/> when it is already in the future,
    /// or when the interval is non-positive (left for validation to reject without dividing by zero).
    /// </summary>
    private static DateTimeOffset AlignForward(DateTimeOffset anchor, TimeSpan interval, DateTimeOffset after)
    {
        if (interval <= TimeSpan.Zero || anchor > after)
            return anchor;

        // UtcTicks, never Ticks: DateTimeOffset.Ticks is the wall-clock reading in the value's own
        // offset, so subtracting two of them silently understates the elapsed time by the offset
        // difference. With an anchor like 09:00+02:00 that lands NextRunAt in the *past*, and the
        // scheduler then re-derives the same past instant on every 60s poll — firing (and billing)
        // the suite once a minute. The `anchor > after` guard above is instant-based, so the two
        // must agree on what "later" means.
        long steps = (after.UtcTicks - anchor.UtcTicks) / interval.Ticks + 1;
        return anchor + TimeSpan.FromTicks(interval.Ticks * steps);
    }

    /// <summary>
    /// Disables.
    /// </summary>
    public Task<ITestRunSchedule> Disable(CancellationToken cancellationToken = default)
        => ApplyAsync(this with { IsEnabled = false }, cancellationToken);

    /// <summary>
    /// Enables.
    /// </summary>
    public Task<ITestRunSchedule> Enable(CancellationToken cancellationToken = default)
        => ApplyAsync(this with { IsEnabled = true }, cancellationToken);

    /// <summary>
    /// Record fired.
    /// </summary>
    public Task<ITestRunSchedule> RecordFired(DateTimeOffset now, CancellationToken cancellationToken = default)
        => ApplyAsync(this with
        {
            LastRunAt = now,
            NextRunAt = AlignForward(AnchorAt, Interval, now),
        }, cancellationToken);

    /// <summary>
    /// Updates.
    /// </summary>
    public Task<ITestRunSchedule> Update(
        string name, IReadOnlyCollection<IModelEndpoint> endpoints,
        TimeSpan interval, bool isEnabled, DateTimeOffset anchorAt, DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => ApplyAsync(this with
        {
            Name = name,
            Endpoints = endpoints.ToArray(),
            Interval = interval,
            IsEnabled = isEnabled,
            AnchorAt = anchorAt,
            // Re-derive the next fire only when the cadence (anchor or interval) actually changes, so
            // a rename or an enable/disable toggle — both of which route through this Update — never
            // advances (and thereby drops) an imminent or already-overdue run.
            NextRunAt = anchorAt != AnchorAt || interval != Interval
                ? AlignForward(anchorAt, interval, now)
                : NextRunAt,
        }, cancellationToken);

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        if (string.IsNullOrWhiteSpace(Name))
            yield return Validation.NotNullOrWhiteSpace(Name);

        if (Interval < TimeSpan.FromMinutes(1))
            yield return new ValidationResult("Schedule interval must be at least one minute.", [nameof(Interval)]);

        if (Endpoints.Count == 0)
            yield return new ValidationResult("A schedule must target at least one endpoint.", [nameof(Endpoints)]);

        if (Endpoints.Count > ITestRunGroup.MaxModelEndpoints)
            yield return new ValidationResult(
                $"A schedule can target at most {ITestRunGroup.MaxModelEndpoints} model endpoints.",
                [nameof(Endpoints)]);

        foreach (var result in Suite.Validate(validationContext))
            yield return result;

        foreach (var result in Endpoints.SelectMany(endpoint => endpoint.Validate(validationContext)))
            yield return result;
    }
}
