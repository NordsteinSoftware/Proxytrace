using Microsoft.EntityFrameworkCore;

namespace Proxytrace.Storage;

/// <summary>
/// Proxytrace's concrete EF Core context. All model-building behaviour — applying the discovered
/// <see cref="IModelConfiguration"/> slices and the <c>UpdatedAt</c> optimistic-concurrency-token
/// convention — lives in the reusable <see cref="NordsteinDbContext"/> base. This type exists to
/// carry the product's identity: it is what <c>DbContextOptions&lt;StorageDbContext&gt;</c> and the
/// migrations assembly are keyed on.
/// </summary>
internal sealed class StorageDbContext : NordsteinDbContext
{
    public StorageDbContext(
        IEnumerable<IModelConfiguration> configurations,
        DbContextOptions<StorageDbContext> options)
        : base(configurations, options)
    {
    }
}
