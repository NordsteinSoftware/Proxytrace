using Nordstein.Core.Domain.Paging;

namespace Proxytrace.Domain.ApplicationError;

/// <summary>
/// Repository for <see cref="IApplicationError"/> with newest-first paging plus the rotation
/// and count-cap operations used by the scheduled cleanup service.
/// </summary>
public interface IApplicationErrorRepository : IRepository<IApplicationError>
{
    /// <summary>
    /// Returns a page of errors ordered newest-first, optionally filtered by <paramref name="level"/>,
    /// a case-insensitive infix <paramref name="search"/> matched against the message and stacktrace,
    /// and an inclusive <paramref name="from"/>..<paramref name="to"/> time window on the capture time.
    /// </summary>
    Task<PagedResult<IApplicationError>> GetPagedNewestFirstAsync(
        int page,
        int pageSize,
        ApplicationErrorLevel? level,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all errors created on or before <paramref name="cutoffDate"/> (age-based rotation).
    /// Returns the number of rows removed.
    /// </summary>
    Task<int> RemoveOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the oldest rows so that at most <paramref name="max"/> newest errors remain
    /// (count cap; bounds the table during an error storm). Returns the number of rows removed.
    /// </summary>
    Task<int> TrimToNewestAsync(int max, CancellationToken cancellationToken = default);
}
