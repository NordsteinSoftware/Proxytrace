namespace Proxytrace.Application.Cleanup;

/// <summary>
/// Service that provides data cleanup functionality.
/// </summary>
public interface IDataCleanupService
{
    Task DeleteAllNonModelDataAsync(CancellationToken cancellationToken = default);
}
