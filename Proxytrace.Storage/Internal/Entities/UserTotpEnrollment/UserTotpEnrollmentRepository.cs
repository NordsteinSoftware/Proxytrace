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
    /// <summary>
    /// Initializes a new instance of the <see cref="UserTotpEnrollmentRepository"/> class.
    /// </summary>
    public UserTotpEnrollmentRepository(
        IMapper<IUserTotpEnrollment, UserTotpEnrollmentEntity> mapper,
        Func<StorageDbContext> context,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, context, transaction, entityEvents, ambient)
    {
    }

    /// <summary>
    /// Finds the by user asynchronously.
    /// </summary>
    public async Task<IUserTotpEnrollment?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var entity = await contextFactory().Set<UserTotpEnrollmentEntity>().AsNoTracking()
            .Where(x => x.User == userId)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(entity, cancellationToken);
    }

    /// <summary>
    /// Lists the confirmed user ids asynchronously.
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> ListConfirmedUserIdsAsync(CancellationToken cancellationToken = default)
        => await contextFactory().Set<UserTotpEnrollmentEntity>().AsNoTracking()
            .Where(x => x.ConfirmedAt != null)
            .Select(x => x.User)
            .ToListAsync(cancellationToken);
}
