using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.Invite;

namespace Proxytrace.Storage.Internal.Entities.Invite;

[UsedImplicitly]
internal class InviteRepository : AbstractRepository<IInvite, InviteEntity>, IInviteRepository
{
    private readonly ISecretHasher hasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteRepository"/> class.
    /// </summary>
    public InviteRepository(
        IMapper<IInvite, InviteEntity> mapper,
        Func<StorageDbContext> context,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        ISecretHasher hasher) : base(mapper, context, transaction, entityEvents, ambient)
    {
        this.hasher = hasher;
    }

    /// <summary>
    /// Finds the by token asynchronously.
    /// </summary>
    public async Task<IInvite?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        // The token is stored as a hash; match on the hash of the presented raw token.
        var tokenHash = hasher.Hash(token);
        var entity = await contextFactory().Set<InviteEntity>().AsNoTracking()
            .Where(x => x.TokenHash == tokenHash)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(entity, cancellationToken);
    }
}
