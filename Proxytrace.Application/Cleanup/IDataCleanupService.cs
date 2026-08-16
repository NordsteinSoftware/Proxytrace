namespace Proxytrace.Application.Cleanup;

/// <summary>
/// Bulk-resets a project's operational data for demo resets and test teardown.
/// </summary>
public interface IDataCleanupService
{
    /// <summary>
    /// Deletes all traces, test runs, statistics, and notifications while preserving model configuration (agents, endpoints, providers, suites).
    /// </summary>
    Task DeleteAllNonModelDataAsync(CancellationToken cancellationToken = default);
}
