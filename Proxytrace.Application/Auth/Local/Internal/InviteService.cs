using Nordstein.Core.Common.Async;
using System.Security.Cryptography;
using Proxytrace.Domain.Security;
using Proxytrace.Domain;
using Proxytrace.Domain.Invite;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;

namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class InviteService : IInviteService
{
    private const int TokenBytes = 32;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    private readonly IInviteRepository invites;
    private readonly IUserRepository users;
    private readonly IInvite.CreateNew createInvite;
    private readonly IUser.CreateNew createUser;
    private readonly IPasswordService passwords;
    private readonly IAsyncLock asyncLock;

    /// <summary>Lock key serializing the licensed-seat check against the invite it authorises.</summary>
    private const string UserQuotaLockKey = "license-quota:users";
    private readonly ITransaction transaction;
    private readonly ILicenseService license;
    private readonly ISecretHasher hasher;

    public InviteService(
        IInviteRepository invites,
        IUserRepository users,
        IInvite.CreateNew createInvite,
        IUser.CreateNew createUser,
        IPasswordService passwords,
        IAsyncLock asyncLock,
        ITransaction transaction,
        ILicenseService license,
        ISecretHasher hasher)
    {
        this.invites = invites;
        this.users = users;
        this.createInvite = createInvite;
        this.createUser = createUser;
        this.passwords = passwords;
        this.asyncLock = asyncLock;
        this.transaction = transaction;
        this.license = license;
        this.hasher = hasher;
    }

    public async Task<InviteCreated> CreateAsync(
        string email,
        UserRole role,
        IUser invitedBy,
        CancellationToken cancellationToken = default)
    {
        // Held for the rest of the method: the count and the invite it authorises are check-then-act,
        // so two concurrent invites both used to observe the pre-invite user count and both proceed,
        // taking an install past its licensed seat limit. Per-process, like the other quota locks.
        using IDisposable quotaLock = await asyncLock.LockAsync(UserQuotaLockKey, cancellationToken);
        license.Ensure(LicenseLimit.MaxUsers, await users.CountAsync(cancellationToken));

        // Persist only the hash of the token; the raw value is returned once so the caller can build
        // the invite link, and is unrecoverable afterwards.
        var rawToken = GenerateToken();
        var invite = createInvite(email, role, hasher.Hash(rawToken), DateTimeOffset.UtcNow + Ttl, invitedBy);
        var saved = await invite.AddAsync(cancellationToken);
        return new InviteCreated(saved, rawToken);
    }

    public async Task<IInvite?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var invite = await invites.FindByTokenAsync(token, cancellationToken);
        if (invite is null)
        {
            return null;
        }

        if (invite.IsConsumed)
        {
            return null;
        }

        return invite.IsExpired(DateTimeOffset.UtcNow) ? null : invite;
    }

    public Task<IUser?> ConsumeAsync(string token, string password, CancellationToken cancellationToken = default)
        => transaction.InvokeAsync<IUser?>(async () =>
        {
            var invite = await GetByTokenAsync(token, cancellationToken);
            if (invite is null)
            {
                return null;
            }

            // Hash against a draft user (PasswordHasher<IUser> in current MS implementation
            // doesn't actually read user state, but contract leaves room — use a draft for safety).
            var draft = createUser(
                invite.Email, 
                externalSubject: null, 
                passwordHash: "placeholder", 
                role: invite.Role);
            
            var hash = passwords.Hash(draft, password);
            var withHash = createUser(
                invite.Email, 
                externalSubject: null,
                passwordHash: hash, 
                role: invite.Role);
            
            var saved = await withHash.AddAsync(cancellationToken);

            await invite.MarkConsumedAsync(cancellationToken);
            return saved;
        });

    private static string GenerateToken()
    {
        var bytes = new byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
