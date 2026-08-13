using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.UserTotpEnrollment;

namespace Proxytrace.Storage.Internal.Entities.UserTotpEnrollment;

[UsedImplicitly]
internal class UserTotpEnrollmentRepository
    : AbstractRepository<IUserTotpEnrollment, UserTotpEnrollmentEntity>,
      IUserTotpEnrollmentRepository
{
    public UserTotpEnrollmentRepository(
        IMapper<IUserTotpEnrollment, UserTotpEnrollmentEntity> mapper,
        Func<StorageDbContext> context,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, context, transaction, entityEvents, ambient)
    {
    }

    public async Task<IUserTotpEnrollment?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var entity = await contextFactory().Set<UserTotpEnrollmentEntity>().AsNoTracking()
            .Where(x => x.User == userId)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(entity, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> ListConfirmedUserIdsAsync(CancellationToken cancellationToken = default)
        => await contextFactory().Set<UserTotpEnrollmentEntity>().AsNoTracking()
            .Where(x => x.ConfirmedAt != null)
            .Select(x => x.User)
            .ToListAsync(cancellationToken);
}
