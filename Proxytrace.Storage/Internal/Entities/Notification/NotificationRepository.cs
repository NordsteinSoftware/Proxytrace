using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Storage.Internal.Entities.Notification;

[UsedImplicitly]
internal class NotificationRepository :
    AbstractRepository<INotification, NotificationEntity>,
    INotificationRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationRepository"/> class.
    /// </summary>
    public NotificationRepository(
        IMapper<INotification, NotificationEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    /// <summary>
    /// Gets the for scope asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<INotification>> GetForScopeAsync(
        Guid? projectId,
        bool includeRead,
        CancellationToken cancellationToken = default)
    {
        var query = contextFactory()
            .Set<NotificationEntity>()
            .AsNoTracking()
            .Where(e => e.Status != NotificationStatus.Dismissed)
            .Where(e => e.ProjectId == null || e.ProjectId == projectId);

        if (!includeRead)
            query = query.Where(e => e.Status == NotificationStatus.Unread);

        var stored = await query
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Counts the unread asynchronously.
    /// </summary>
    public async Task<int> CountUnreadAsync(
        Guid? projectId,
        CancellationToken cancellationToken = default)
        => await contextFactory()
            .Set<NotificationEntity>()
            .AsNoTracking()
            .Where(e => e.Status == NotificationStatus.Unread)
            .Where(e => e.ProjectId == null || e.ProjectId == projectId)
            .CountAsync(cancellationToken);

    /// <summary>
    /// Finds the active by target asynchronously.
    /// </summary>
    public async Task<INotification?> FindActiveByTargetAsync(
        NotificationTargetKind targetKind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<NotificationEntity>()
            .AsNoTracking()
            .Where(e => e.TargetKind == targetKind
                        && e.TargetId == targetId
                        && e.Status != NotificationStatus.Dismissed)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }
}
