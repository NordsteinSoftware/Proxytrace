using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.MfaBackupCode;

namespace Proxytrace.Storage.Internal.Entities.MfaBackupCode;

[UsedImplicitly]
internal class MfaBackupCodeRepository
    : AbstractRepository<IMfaBackupCode, MfaBackupCodeEntity>,
      IMfaBackupCodeRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MfaBackupCodeRepository"/> class.
    /// </summary>
    public MfaBackupCodeRepository(
        IMapper<IMfaBackupCode, MfaBackupCodeEntity> mapper,
        Func<StorageDbContext> context,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, context, transaction, entityEvents, ambient)
    {
    }

    /// <summary>
    /// Lists the by user asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<IMfaBackupCode>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory().Set<MfaBackupCodeEntity>().AsNoTracking()
            .Where(x => x.User == userId)
            .ToListAsync(cancellationToken);
        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Finds the by code hash asynchronously.
    /// </summary>
    public async Task<IMfaBackupCode?> FindByCodeHashAsync(Guid userId, string codeHash, CancellationToken cancellationToken = default)
    {
        var entity = await contextFactory().Set<MfaBackupCodeEntity>().AsNoTracking()
            .Where(x => x.User == userId && x.CodeHash == codeHash)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(entity, cancellationToken);
    }
}
