using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Domain;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunGroup.Internal;

internal record TestRunGroup : DomainEntity<ITestRunGroup>, ITestRunGroup
{
    private readonly ITestRunRepository testRuns;

    public ITestSuite Suite { get; }
    public TestRunStatus Status { get; private init; }
    public DateTimeOffset? CompletedAt { get; private init; }
    public bool IsSystemRun { get; }
    public Guid? ScheduleId { get; }
    public int SampleCount { get; }
    public DateTimeOffset? OptimizationConsideredAt { get; private init; }

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

    public Task<ITestRunGroup> MarkOptimizationConsidered(CancellationToken cancellationToken = default)
        => ApplyAsync(this with { OptimizationConsideredAt = DateTimeOffset.UtcNow }, cancellationToken);

    public Task<IReadOnlyList<ITestRun>> GetTestRuns(CancellationToken cancellationToken = default)
        => testRuns.GetByGroupAsync(Id, cancellationToken);

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

    public Task<ITestRunGroup> SetRunning(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Running, cancellationToken);

    public Task<ITestRunGroup> SetCompleted(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Completed, cancellationToken);

    public Task<ITestRunGroup> SetFailed(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Failed, cancellationToken);

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
