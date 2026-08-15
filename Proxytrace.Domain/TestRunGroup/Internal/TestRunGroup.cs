using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Domain;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunGroup.Internal;

internal record TestRunGroup : DomainEntity<ITestRunGroup>, ITestRunGroup
{
    private readonly ITestRunRepository testRuns;

    /// <summary>
    /// Gets the suite.
    /// </summary>
    public ITestSuite Suite { get; }
    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public TestRunStatus Status { get; private init; }
    /// <summary>
    /// Gets or sets the completed at.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; private init; }
    /// <summary>
    /// Gets the is system run.
    /// </summary>
    public bool IsSystemRun { get; }
    /// <summary>
    /// Gets the schedule id.
    /// </summary>
    public Guid? ScheduleId { get; }
    /// <summary>
    /// Gets the sample count.
    /// </summary>
    public int SampleCount { get; }
    /// <summary>
    /// Gets or sets the optimization considered at.
    /// </summary>
    public DateTimeOffset? OptimizationConsideredAt { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunGroup"/> class.
    /// </summary>
    public TestRunGroup(
        ITestSuite suite,
        bool isSystemRun,
        Guid? scheduleId,
        int sampleCount,
        IRepository<ITestRunGroup> repository,
        ITestRunRepository testRuns) : base(repository)
    {
        this.testRuns = testRuns;
        Suite = suite;
        Status = TestRunStatus.Pending;
        CompletedAt = null;
        IsSystemRun = isSystemRun;
        ScheduleId = scheduleId;
        SampleCount = sampleCount;
        OptimizationConsideredAt = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunGroup"/> class.
    /// </summary>
    public TestRunGroup(
        ITestSuite suite,
        TestRunStatus status,
        DateTimeOffset? completedAt,
        bool isSystemRun,
        Guid? scheduleId,
        int sampleCount,
        DateTimeOffset? optimizationConsideredAt,
        IDomainEntityData existing,
        IRepository<ITestRunGroup> repository,
        ITestRunRepository testRuns) : base(existing, repository)
    {
        this.testRuns = testRuns;
        Suite = suite;
        Status = status;
        CompletedAt = completedAt;
        IsSystemRun = isSystemRun;
        ScheduleId = scheduleId;
        SampleCount = sampleCount;
        OptimizationConsideredAt = optimizationConsideredAt;
    }

    /// <summary>
    /// Mark optimization considered.
    /// </summary>
    public Task<ITestRunGroup> MarkOptimizationConsidered(CancellationToken cancellationToken = default)
        => ApplyAsync(this with { OptimizationConsideredAt = DateTimeOffset.UtcNow }, cancellationToken);

    /// <summary>
    /// Gets the test runs.
    /// </summary>
    public Task<IReadOnlyList<ITestRun>> GetTestRuns(CancellationToken cancellationToken = default)
        => testRuns.GetByGroupAsync(Id, cancellationToken);

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Suite.Validate(validationContext))
            yield return result;

        if (SampleCount is < 1 or > ITestRunGroup.MaxSampleCount)
        {
            yield return new ValidationResult(
                $"Sample count must be between 1 and {ITestRunGroup.MaxSampleCount}.",
                [nameof(SampleCount)]);
        }
    }

    /// <summary>
    /// Sets the running.
    /// </summary>
    public Task<ITestRunGroup> SetRunning(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Running, cancellationToken);

    /// <summary>
    /// Sets the completed.
    /// </summary>
    public Task<ITestRunGroup> SetCompleted(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Completed, cancellationToken);

    /// <summary>
    /// Sets the failed.
    /// </summary>
    public Task<ITestRunGroup> SetFailed(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Failed, cancellationToken);

    /// <summary>
    /// Sets the cancelled.
    /// </summary>
    public Task<ITestRunGroup> SetCancelled(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Cancelled, cancellationToken);

    private Task<ITestRunGroup> SetState(TestRunStatus state, CancellationToken cancellationToken)
    {
        if (Status.IsTerminal())
        {
            throw new InvalidOperationException(
                $"Cannot change test run group {Id} status from {Status} to {state} because it is already in a terminal state.");
        }

        DateTimeOffset? completedAt = state.IsTerminal() ? DateTimeOffset.UtcNow : null;
        return ApplyAsync(this with { Status = state, CompletedAt = completedAt }, cancellationToken);
    }
}
