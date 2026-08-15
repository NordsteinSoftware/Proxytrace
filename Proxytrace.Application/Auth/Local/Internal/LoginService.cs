using Proxytrace.Domain.User;
using Proxytrace.Domain.UserTotpEnrollment;

namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class LoginService : ILoginService
{
    private readonly IUserRepository users;
    private readonly IPasswordService passwords;
    private readonly ILocalTokenIssuer tokens;
    private readonly IUserTotpEnrollmentRepository enrollments;
    private readonly IMfaChallengeService challenges;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginService"/> class.
    /// </summary>
    public LoginService(
        IUserRepository users,
        IPasswordService passwords,
        ILocalTokenIssuer tokens,
        IUserTotpEnrollmentRepository enrollments,
        IMfaChallengeService challenges)
    {
        this.users = users;
        this.passwords = passwords;
        this.tokens = tokens;
        this.enrollments = enrollments;
        this.challenges = challenges;
    }

    /// <summary>
    /// Login asynchronously.
    /// </summary>
    public async Task<LoginOutcome?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email, cancellationToken);
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            // Spend the same PBKDF2 cost the found-user path spends below. Returning here without
            // hashing made an unknown email answer in a fraction of the time a known one took,
            // disclosing which addresses have accounts (a user-enumeration oracle).
            passwords.VerifyDummy(password);
            return null;
        }

        if (!passwords.Verify(user, user.PasswordHash, password))
        {
            return null;
        }

        // Password OK. If the account has confirmed TOTP MFA, defer the session to the second step.
        if (await enrollments.FindByUserAsync(user.Id, cancellationToken) is { IsConfirmed: true })
        {
            var challenge = challenges.Issue(user);
            return new MfaRequired(user, challenge.Token, challenge.ExpiresAt);
        }

        var issued = tokens.Issue(user);
        return new LoginSucceeded(user, issued.Token, issued.ExpiresAt);
    }
}
