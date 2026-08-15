using Proxytrace.Domain.Statistics;
using Proxytrace.Domain;

namespace Proxytrace.Application.Statistics.Internal;

internal abstract class AbstractStatsProjector<TDomainEntity, TStats> : IStatsProjector
    where TDomainEntity : IDomainEntity
{
    private readonly IStatsWriter<TStats> writer;
    private readonly IRepository<TDomainEntity> repository;

    /// <summary>
    /// Gets the entity type.
    /// </summary>
    public Type EntityType => typeof(TDomainEntity);

    protected AbstractStatsProjector(
        IStatsWriter<TStats> writer,
        IRepository<TDomainEntity> repository)
    {
        this.writer = writer;
        this.repository = repository;
    }

    /// <summary>
    /// Project asynchronously.
    /// </summary>
    public async Task ProjectAsync(Guid entityId, CancellationToken cancellationToken)
    {
        TDomainEntity? entity = await repository.FindAsync(entityId, cancellationToken);
        if (entity is null || !ShouldProject(entity))
        {
            // Removing rather than merely skipping matters: an entity that should not be projected
            // may already have a row from before it was excluded, and leaving that row behind would
            // keep it in the statistics forever.
            await writer.RemoveAsync(entityId, cancellationToken);
            return;
        }

        TStats stats = await ComputeStatsAsync(entity, cancellationToken);
        await writer.UpsertAsync(stats, cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="entity"/> belongs in the user-facing statistics at all. Default:
    /// everything does.
    /// </summary>
    protected virtual bool ShouldProject(TDomainEntity entity) => true;

    protected abstract Task<TStats> ComputeStatsAsync(TDomainEntity entity, CancellationToken cancellationToken);
}
