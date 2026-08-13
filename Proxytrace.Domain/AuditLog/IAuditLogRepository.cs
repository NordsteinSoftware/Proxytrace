using Nordstein.Core.Domain.Paging;

namespace Proxytrace.Domain.AuditLog;

/// <summary>
/// Repository for <see cref="IAuditLogEntry"/> with newest-first paging plus age-based retention.
/// The audit log is lossless: there is no count cap.
/// </summary>
public interface IAuditLogRepository : IRepository<IAuditLogEntry>
{
    /// <summary>
    /// Returns a page of audit entries ordered newest-first, optionally filtered by
    /// <paramref name="action"/>, a case-insensitive infix <paramref name="actorSearch"/> on the
    /// actor email, <paramref name="targetType"/>/<paramref name="targetId"/>, and an inclusive
    /// <paramref name="from"/>..<paramref name="to"/> window on the event time.
    /// </summary>
    /// <param name="projectIds">
    /// When <see langword="null"/>, no project restriction is applied (admin view — sees every
    /// project plus global rows). When non-null, only rows whose <c>ProjectId</c> is in the set are
    /// returned, plus global (null-project) rows only if <paramref name="includeGlobal"/> is true.
    /// </param>
    /// <param name="includeGlobal">
    /// Whether global (null-<c>ProjectId</c>) rows are included when <paramref name="projectIds"/>
    /// restricts the result. Ignored when <paramref name="projectIds"/> is <see langword="null"/>.
    /// </param>
    Task<PagedResult<IAuditLogEntry>> GetPagedNewestFirstAsync(
        int page,
        int pageSize,
        AuditAction? action,
        string? actorSearch,
        IReadOnlyCollection<Guid>? projectIds,
        bool includeGlobal,
        string? targetType,
        Guid? targetId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all entries created on or before <paramref name="cutoffDate"/> (age-based retention).
    /// Returns the number of rows removed.
    /// </summary>
    Task<int> RemoveOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default);
}
