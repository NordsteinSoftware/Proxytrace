using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.PasswordResetToken;

namespace Proxytrace.Storage.Internal.Entities.PasswordResetToken;

[UsedImplicitly]
internal class PasswordResetTokenRepository
    : AbstractRepository<IPasswordResetToken, PasswordResetTokenEntity>,
      IPasswordResetTokenRepository
{
    private readonly ISecretHasher hasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordResetTokenRepository"/> class.
    /// </summary>
    public PasswordResetTokenRepository(
        IMapper<IPasswordResetToken, PasswordResetTokenEntity> mapper,
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
    public async Task<IPasswordResetToken?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        // The token is stored as a hash; match on the hash of the presented raw token.
        var tokenHash = hasher.Hash(token);
        var entity = await contextFactory().Set<PasswordResetTokenEntity>().AsNoTracking()
            .Where(x => x.TokenHash == tokenHash)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(entity, cancellationToken);
    }
}
