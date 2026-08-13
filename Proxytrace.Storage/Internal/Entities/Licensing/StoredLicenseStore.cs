using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain.Licensing;
using Nordstein.Core.Common.Time;

namespace Proxytrace.Storage.Internal.Entities.Licensing;

[UsedImplicitly]
internal sealed class StoredLicenseStore : IStoredLicenseStore
{
    private readonly Func<StorageDbContext> contextFactory;
    private readonly IClock clock;

    public StoredLicenseStore(Func<StorageDbContext> contextFactory, IClock clock)
    {
        this.contextFactory = contextFactory;
        this.clock = clock;
    }

    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        StoredLicenseEntity? entity = await contextFactory()
            .Set<StoredLicenseEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        return entity?.Jwt;
    }

    public async Task SaveAsync(string licenseJwt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseJwt);

        StorageDbContext context = contextFactory();
        DbSet<StoredLicenseEntity> set = context.Set<StoredLicenseEntity>();
        var now = clock.UtcNow;

        StoredLicenseEntity? existing = await set.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            set.Add(new StoredLicenseEntity
            {
                Id = Guid.NewGuid(),
                Jwt = licenseJwt,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(
                existing with { Jwt = licenseJwt, UpdatedAt = now });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        DbSet<StoredLicenseEntity> set = context.Set<StoredLicenseEntity>();

        StoredLicenseEntity? existing = await set.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
            return;

        set.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }
}
