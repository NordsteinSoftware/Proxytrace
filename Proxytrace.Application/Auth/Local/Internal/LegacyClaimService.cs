using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class LegacyClaimService : ILegacyClaimService
{
    private const string LegacyExternalSubjectPrefix = "legacy:";

    private readonly IUserRepository users;
    private readonly IPasswordService passwords;
    private readonly ILocalTokenIssuer tokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyClaimService"/> class.
    /// </summary>
    public LegacyClaimService(
        IUserRepository users,
        IPasswordService passwords,
        ILocalTokenIssuer tokens)
    {
        this.users = users;
        this.passwords = passwords;
        this.tokens = tokens;
    }

    /// <summary>
    /// Determines whether the claim available asynchronously.
    /// </summary>
    public async Task<bool> IsClaimAvailableAsync(CancellationToken cancellationToken = default)
        => await FindEligibleAsync(cancellationToken) is not null;

    /// <summary>
    /// Claim asynchronously.
    /// </summary>
    public async Task<LoginResult?> ClaimAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var eligible = await FindEligibleAsync(cancellationToken);
        if (eligible is null) return null;

        if (!string.Equals(eligible.Email, email, StringComparison.OrdinalIgnoreCase))
            return null;

        var hash = passwords.Hash(eligible, password);
        var updated = await eligible.ChangePasswordHash(hash, cancellationToken);

        var issued = tokens.Issue(updated);
        return new LoginResult(updated, issued.Token, issued.ExpiresAt);
    }

    private async Task<IUser?> FindEligibleAsync(CancellationToken cancellationToken)
    {
        if (await users.CountAsync(cancellationToken) != 1) return null;

        var all = await users.GetAllAsync(cancellationToken);
        var only = all[0];

        if (!string.IsNullOrEmpty(only.PasswordHash)) return null;
        if (only.ExternalSubject is null) return null;
        return !only.ExternalSubject.StartsWith(LegacyExternalSubjectPrefix, StringComparison.Ordinal) ? null : only;
    }
}
