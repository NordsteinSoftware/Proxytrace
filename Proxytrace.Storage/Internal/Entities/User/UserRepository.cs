using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.User;

namespace Proxytrace.Storage.Internal.Entities.User;

[UsedImplicitly]
internal class UserRepository : AbstractRepository<IUser, UserEntity>, IUserRepository
{
    public UserRepository(
        IMapper<IUser, UserEntity> mapper,
        Func<StorageDbContext> context,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, context, transaction, entityEvents, ambient) { }

    public async Task<IUser?> FindByExternalSubjectAsync(string externalSubject, CancellationToken cancellationToken = default)
    {
        var entity = await contextFactory().Set<UserEntity>().AsNoTracking()
            .Where(x => x.ExternalSubject == externalSubject)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(entity, cancellationToken);
    }

    public async Task<IUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // Emails are normalized (trimmed, invariant-lowercase) at the write boundary (see User ctor),
        // so normalizing the input the same way and matching the stored value exactly lets PostgreSQL
        // use the plain unique B-tree index on Email — a LOWER(Email) = LOWER(@x) predicate would
        // instead force a sequential scan on every login.
        var normalized = email.Trim().ToLowerInvariant();
        var entity = await contextFactory().Set<UserEntity>().AsNoTracking()
            .Where(x => x.Email == normalized)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(entity, cancellationToken);
    }

    public async Task<int> CountByRoleAsync(UserRole role, CancellationToken cancellationToken = default)
        => await contextFactory().Set<UserEntity>().AsNoTracking()
            .CountAsync(x => x.Role == role, cancellationToken);
}
