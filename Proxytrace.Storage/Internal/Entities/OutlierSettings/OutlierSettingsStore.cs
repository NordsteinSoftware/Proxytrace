using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Time;
using AppOutlierSettings = Proxytrace.Domain.Outliers.OutlierSettings;
using IOutlierSettingsStore = Proxytrace.Domain.Outliers.IOutlierSettingsStore;

namespace Proxytrace.Storage.Internal.Entities.OutlierSettings;

[UsedImplicitly]
internal sealed class OutlierSettingsStore : IOutlierSettingsStore
{
    private readonly Func<StorageDbContext> contextFactory;
    private readonly IClock clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutlierSettingsStore"/> class.
    /// </summary>
    public OutlierSettingsStore(Func<StorageDbContext> contextFactory, IClock clock)
    {
        this.contextFactory = contextFactory;
        this.clock = clock;
    }

    /// <summary>
    /// Gets asynchronously.
    /// </summary>
    public async Task<AppOutlierSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        OutlierSettingsEntity? entity = await contextFactory()
            .Set<OutlierSettingsEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return new AppOutlierSettings(
            entity.Enabled, entity.SigmaMultiplier, entity.MinSampleCount, entity.SampleWindow);
    }

    /// <summary>
    /// Saves asynchronously.
    /// </summary>
    public async Task SaveAsync(AppOutlierSettings settings, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        DbSet<OutlierSettingsEntity> set = context.Set<OutlierSettingsEntity>();
        var now = clock.UtcNow;

        OutlierSettingsEntity? existing = await set.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            set.Add(new OutlierSettingsEntity
            {
                Id = Guid.NewGuid(),
                Enabled = settings.Enabled,
                SigmaMultiplier = settings.SigmaMultiplier,
                MinSampleCount = settings.MinSampleCount,
                SampleWindow = settings.SampleWindow,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(existing with
            {
                Enabled = settings.Enabled,
                SigmaMultiplier = settings.SigmaMultiplier,
                MinSampleCount = settings.MinSampleCount,
                SampleWindow = settings.SampleWindow,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
