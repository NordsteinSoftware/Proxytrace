using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Cleanup.Internal;

internal class DataCleanupService : IDataCleanupService
{
    private readonly IRepository<IOptimizationProposal> proposals;
    private readonly IRepository<ITestResult> testResults;
    private readonly IRepository<ITestRun> testRuns;
    private readonly IRepository<ITestRunGroup> testRunGroups;
    private readonly IRepository<IAgentCall> agentCalls;
    private readonly ITransaction transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataCleanupService"/> class.
    /// </summary>
    public DataCleanupService(
        IRepository<IOptimizationProposal> proposals,
        IRepository<ITestResult> testResults,
        IRepository<ITestRun> testRuns,
        IRepository<ITestRunGroup> testRunGroups,
        IRepository<IAgentCall> agentCalls,
        ITransaction transaction)
    {
        this.proposals = proposals;
        this.testResults = testResults;
        this.testRuns = testRuns;
        this.testRunGroups = testRunGroups;
        this.agentCalls = agentCalls;
        this.transaction = transaction;
    }

    /// <summary>
    /// Deletes the all non model data asynchronously.
    /// </summary>
    public Task DeleteAllNonModelDataAsync(CancellationToken cancellationToken = default)
        => transaction.InvokeAsync(async () =>
        {
            await proposals.RemoveAllAsync(cancellationToken);
            await testRuns.RemoveAllAsync(cancellationToken);
            await testRunGroups.RemoveAllAsync(cancellationToken);
            await testResults.RemoveAllAsync(cancellationToken);
            await agentCalls.RemoveAllAsync(cancellationToken);
        });
}
