namespace Proxytrace.Domain.Statistics;

/// <summary>
/// Writes stats data.
/// </summary>
public interface IStatsWriter<TStats>
{
    Task UpsertAsync(TStats stats, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
