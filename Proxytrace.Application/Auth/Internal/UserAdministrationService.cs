using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Internal;

internal sealed class UserAdministrationService : IUserAdministrationService
{
    private readonly IUserRepository users;
    private readonly IApiKeyRepository apiKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserAdministrationService"/> class.
    /// </summary>
    public UserAdministrationService(IUserRepository users, IApiKeyRepository apiKeys)
    {
        this.users = users;
        this.apiKeys = apiKeys;
    }

    /// <summary>
    /// Change role asynchronously.
    /// </summary>
    public async Task<IUser?> ChangeRoleAsync(
        Guid actingUserId,
        Guid targetUserId,
        UserRole newRole,
        CancellationToken cancellationToken = default)
    {
        var target = await users.FindAsync(targetUserId, cancellationToken);
        if (target is null)
            return null;

        if (target.Role == newRole)
            return target;

        if (targetUserId == actingUserId)
            throw new UserAdministrationException("You cannot change your own role.");

        var demotesFromAdmin = target.Role == UserRole.Admin && newRole != UserRole.Admin;
        if (demotesFromAdmin && await IsLastAdminAsync(cancellationToken))
            throw new UserAdministrationException("At least one Admin must remain.");

        return await target.ChangeRole(newRole, cancellationToken);
    }

    /// <summary>
    /// Removes asynchronously.
    /// </summary>
    public async Task<bool> RemoveAsync(
        Guid actingUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var target = await users.FindAsync(targetUserId, cancellationToken);
        if (target is null)
            return false;

        if (targetUserId == actingUserId)
            throw new UserAdministrationException("You cannot delete your own account.");

        if (target.Role == UserRole.Admin && await IsLastAdminAsync(cancellationToken))
            throw new UserAdministrationException("At least one Admin must remain.");

        // Keys owned by this user used to be cascade-deleted along with them. Only the hash is
        // stored, so those keys are unrecoverable — offboarding an admin who happened to mint an
        // integration key silently revoked it, and the failure surfaced later as unexplained 401s in
        // whatever was using it. The FK is now Restrict, so this check turns what would be an opaque
        // database error into an actionable 409 naming the keys to deal with first.
        var ownedKeys = await apiKeys.GetKeyNamesByOwnerAsync(targetUserId, cancellationToken);
        if (ownedKeys.Count > 0)
        {
            throw new UserAdministrationException(
                $"This user owns {ownedKeys.Count} API key(s): {string.Join(", ", ownedKeys)}. "
                + "Delete or reassign them before deleting the user — an API key cannot be recovered "
                + "once removed, and anything using it will stop working.");
        }

        return await users.RemoveAsync(targetUserId, cancellationToken);
    }

    private async Task<bool> IsLastAdminAsync(CancellationToken cancellationToken)
        => await users.CountByRoleAsync(UserRole.Admin, cancellationToken) <= 1;
}
