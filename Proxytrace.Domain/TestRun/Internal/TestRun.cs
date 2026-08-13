using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Domain.TestRun.Internal;

internal record TestRun : DomainEntity<ITestRun>, ITestRun
{
    public ITestRunGroup Group { get; init; }
    public IModelEndpoint Endpoint { get; init; }
    public int SampleIndex { get; init; }
    public TestRunStatus Status { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyList<ITestResult> TestResults { get; init; }

    public TestRun(
        ITestRunGroup group,
        IModelEndpoint endpoint,
        int sampleIndex,
        IRepository<ITestRun> repository) : base(repository)
    {
        Group = group;
        Endpoint = endpoint;
        SampleIndex = sampleIndex;
        Status = TestRunStatus.Pending;
        CompletedAt = null;
        TestResults = [];
    }

    public TestRun(
        ITestRunGroup group,
        IModelEndpoint endpoint,
        int sampleIndex,
        TestRunStatus status,
        DateTimeOffset? completedAt,
        IReadOnlyList<ITestResult> testResults,
        IDomainEntityData existing,
        IRepository<ITestRun> repository) : base(existing, repository)
    {
        Group = group;
        Endpoint = endpoint;
        SampleIndex = sampleIndex;
        Status = status;
        CompletedAt = completedAt;
        TestResults = testResults.ToArray();
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Group.Validate(validationContext))
            yield return result;

        foreach (var result in Endpoint.Validate(validationContext))
            yield return result;

        foreach (var result in TestResults.SelectMany(x => x.Validate(validationContext)))
            yield return result;

        if (Status == TestRunStatus.Completed)
        {
            yield return Validation.NotNull(CompletedAt);
        }
    }

    public Task<ITestRun> SetTestResult(ITestResult testResult, CancellationToken cancellationToken = default)
    {
        // A case can still finish in-flight after the run reached a terminal state (e.g. during
        // cooperative cancellation). Never resurrect a terminal run or overwrite its CompletedAt:
        // drop the late result and return the run unchanged rather than transitioning it back.
        if (Status.IsTerminal())
        {
            return Task.FromResult<ITestRun>(this);
        }

        IReadOnlyList<ITestResult> updatedResults =
        [
            ..TestResults.Where(x => x.TestCase.Id != testResult.TestCase.Id),
            testResult
        ];

        bool isCompleted = updatedResults.Count == Group.Suite.TestCases.Count;
        DateTimeOffset? completedAt = isCompleted ? DateTimeOffset.UtcNow : null;
        TestRunStatus status = isCompleted ? TestRunStatus.Completed : TestRunStatus.Running;

        return ApplyAsync(this with
        {
            Status = status,
            CompletedAt = completedAt,
            TestResults = updatedResults,
        }, cancellationToken);
    }

    public Task<ITestRun> SetRunning(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Running, cancellationToken);

    public Task<ITestRun> SetCompleted(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Completed, cancellationToken);

    public Task<ITestRun> SetFailed(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Failed, cancellationToken);

    public Task<ITestRun> SetCancelled(CancellationToken cancellationToken = default)
        => SetState(TestRunStatus.Cancelled, cancellationToken);

    private Task<ITestRun> SetState(TestRunStatus state, CancellationToken cancellationToken = default)
    {
        if (state == TestRunStatus.Running && Status != TestRunStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot set test run {Id} to running because it is not in pending status.");
        }

        DateTimeOffset? completedAt = null;
        if (state.IsTerminal())
        {
            if (Status.IsTerminal())
            {
                throw new InvalidOperationException(
                    $"Cannot change test run {Id} status from {Status} to {state} because it is already in a terminal state.");
            }

            if (CompletedAt.HasValue)
            {
                throw new InvalidOperationException(
                    $"Cannot set test run {Id} to {state} because it already has a completion time.");
            }

            completedAt = DateTimeOffset.UtcNow;
        }

        return ApplyAsync(this with { Status = state, CompletedAt = completedAt }, cancellationToken);
    }
}
