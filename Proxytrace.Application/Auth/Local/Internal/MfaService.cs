using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain;
using Proxytrace.Domain.MfaBackupCode;
using Proxytrace.Domain.User;
using Proxytrace.Domain.UserTotpEnrollment;

namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class MfaService : IMfaService
{
    private const int BackupCodeCount = 10;
    private const int BackupCodeChars = 10;

    // PostgreSQL SQLSTATE 23505 = unique_violation. The only unique index a setup insert can trip is
    // IX_UserTotpEnrollmentEntity_User (one enrollment per user), so this unambiguously means a
    // concurrent setup for the same user beat us to it.
    private const string UniqueViolation = "23505";

    // 32-symbol alphabet without the visually ambiguous I/O/0/1.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IUserTotpEnrollmentRepository enrollments;
    private readonly IMfaBackupCodeRepository backupCodes;
    private readonly IUserTotpEnrollment.CreateNew createEnrollment;
    private readonly IMfaBackupCode.CreateNew createBackupCode;
    private readonly ITotpService totp;
    private readonly IMfaChallengeService challenges;
    private readonly IUserRepository users;
    private readonly ILocalTokenIssuer tokenIssuer;
    private readonly ISecretHasher hasher;
    private readonly IPasswordService passwords;
    private readonly ITransaction transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="MfaService"/> class.
    /// </summary>
    public MfaService(
        IUserTotpEnrollmentRepository enrollments,
        IMfaBackupCodeRepository backupCodes,
        IUserTotpEnrollment.CreateNew createEnrollment,
        IMfaBackupCode.CreateNew createBackupCode,
        ITotpService totp,
        IMfaChallengeService challenges,
        IUserRepository users,
        ILocalTokenIssuer tokenIssuer,
        ISecretHasher hasher,
        IPasswordService passwords,
        ITransaction transaction)
    {
        this.enrollments = enrollments;
        this.backupCodes = backupCodes;
        this.createEnrollment = createEnrollment;
        this.createBackupCode = createBackupCode;
        this.totp = totp;
        this.challenges = challenges;
        this.users = users;
        this.tokenIssuer = tokenIssuer;
        this.hasher = hasher;
        this.passwords = passwords;
        this.transaction = transaction;
    }

    /// <summary>
    /// Determines whether the enabled asynchronously.
    /// </summary>
    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default)
        => await enrollments.FindByUserAsync(userId, cancellationToken) is { IsConfirmed: true };

    /// <summary>
    /// Setup asynchronously.
    /// </summary>
    public async Task<MfaSetup?> SetupAsync(IUser user, CancellationToken cancellationToken = default)
    {
        var existing = await enrollments.FindByUserAsync(user.Id, cancellationToken);
        if (existing is { IsConfirmed: true })
        {
            // Already enabled — re-enrolling requires an explicit disable first.
            return null;
        }

        var secret = totp.GenerateSecret();
        try
        {
            return await transaction.InvokeAsync(async () =>
            {
                // Replace any stale, never-confirmed enrollment so a user can restart setup cleanly.
                if (existing is not null)
                {
                    await existing.RemoveAsync(cancellationToken);
                }

                var enrollment = createEnrollment(user, secret);
                await enrollment.AddAsync(cancellationToken);
                return new MfaSetup(secret, totp.BuildOtpAuthUri(user.Email, secret));
            }, cancellationToken);
        }
        catch (Exception ex) when (IsPerUserUniqueViolation(ex))
        {
            // A concurrent setup (e.g. a double-submitted request) won the race against the per-user
            // unique index; our transaction rolled back, so nothing of ours persisted. Return the
            // enrollment that actually landed so the response matches stored state and the user's
            // authenticator scans a secret that will verify — never a 500. If it has since been
            // confirmed, fall through to the "already enabled" contract (null → 409).
            var raced = await enrollments.FindByUserAsync(user.Id, cancellationToken);
            return raced is { IsConfirmed: false }
                ? new MfaSetup(raced.Secret, totp.BuildOtpAuthUri(user.Email, raced.Secret))
                : null;
        }
    }

    // Walks the inner-exception chain for a unique-constraint violation, identified through the BCL
    // DbException.SqlState rather than the Npgsql/EF type so the application layer keeps no hard
    // provider reference. Mirrors AgentCallProcessor.IsRetryable.
    private static bool IsPerUserUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is DbException { SqlState: UniqueViolation })
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Activate asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ActivateAsync(IUser user, string code, CancellationToken cancellationToken = default)
    {
        var enrollment = await enrollments.FindByUserAsync(user.Id, cancellationToken);
        if (enrollment is null || enrollment.IsConfirmed)
        {
            return null;
        }

        if (!totp.TryVerify(enrollment.Secret, code, enrollment.LastUsedStep, out var matchedStep))
        {
            return null;
        }

        return await transaction.InvokeAsync<IReadOnlyList<string>>(async () =>
        {
            await enrollment.Confirm(matchedStep, cancellationToken);
            var (display, hashes) = GenerateBackupCodes(user);
            foreach (var hash in hashes)
            {
                await createBackupCode(user, hash).AddAsync(cancellationToken);
            }
            return display;
        }, cancellationToken);
    }

    /// <summary>
    /// Disables asynchronously.
    /// </summary>
    public async Task<bool?> DisableAsync(IUser user, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(user.PasswordHash) || !passwords.Verify(user, user.PasswordHash, password))
        {
            return null;
        }

        return await RemoveEnrollmentAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Admin disable asynchronously.
    /// </summary>
    public Task<bool> AdminDisableAsync(Guid userId, CancellationToken cancellationToken = default)
        => RemoveEnrollmentAsync(userId, cancellationToken);

    /// <summary>
    /// Verifies the challenge asynchronously.
    /// </summary>
    public async Task<LoginResult?> VerifyChallengeAsync(string challengeToken, string code, CancellationToken cancellationToken = default)
    {
        var userId = challenges.Peek(challengeToken);
        if (userId is null)
        {
            return null;
        }

        var user = await users.FindAsync(userId.Value, cancellationToken);
        var enrollment = user is null ? null : await enrollments.FindByUserAsync(user.Id, cancellationToken);
        if (user is null || enrollment is not { IsConfirmed: true })
        {
            challenges.Consume(challengeToken);
            return null;
        }

        // Primary path: a current TOTP code.
        if (totp.TryVerify(enrollment.Secret, code, enrollment.LastUsedStep, out var matchedStep))
        {
            await enrollment.RecordUsedStep(matchedStep, cancellationToken);
            challenges.Consume(challengeToken);
            return Issue(user);
        }

        // Fallback: a one-time backup code.
        var normalized = NormalizeBackupCode(code);
        if (normalized.Length > 0)
        {
            var backup = await FindMatchingBackupCodeAsync(user, normalized, cancellationToken);
            if (backup is not null)
            {
                await backup.MarkConsumedAsync(cancellationToken);
                challenges.Consume(challengeToken);
                return Issue(user);
            }
        }

        challenges.RegisterFailure(challengeToken);
        return null;
    }

    /// <summary>
    /// Finds the user's unconsumed backup code matching <paramref name="normalized"/>, or
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Codes are hashed with a <b>per-code salt</b>, so there is no hash to look the row up by —
    /// the candidates have to be loaded and verified one at a time. That is the point: the previous
    /// single-round unsalted SHA-256 supported a direct indexed lookup precisely because every code
    /// in the installation hashed under the same function, which is what let one dump be attacked
    /// against every user's codes simultaneously.
    /// </para>
    /// <para>
    /// The scan is bounded by <see cref="BackupCodeCount"/> (10) and only runs after a TOTP code has
    /// already failed, so the added verification cost lands on a rare recovery path, never on the
    /// normal login. Consumed codes are filtered out first so a redeemed code is never re-verified.
    /// </para>
    /// </remarks>
    private async Task<IMfaBackupCode?> FindMatchingBackupCodeAsync(
        IUser user,
        string normalized,
        CancellationToken cancellationToken)
    {
        var candidates = await backupCodes.ListByUserAsync(user.Id, cancellationToken);
        foreach (var candidate in candidates)
        {
            if (candidate.IsConsumed)
            {
                continue;
            }

            if (Matches(user, candidate.CodeHash, normalized))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Verifies a presented code against a stored hash, accepting both the current salted PBKDF2
    /// form and the legacy unsalted SHA-256 that predates it.
    /// </summary>
    /// <remarks>
    /// Legacy rows are not rewritten: the raw code is unrecoverable, so a stored hash can only be
    /// upgraded when the user redeems it — and redeeming consumes it, leaving nothing to upgrade.
    /// Existing codes therefore keep working until they are used or the user regenerates them by
    /// re-enrolling; only newly issued batches get the stronger hash.
    /// </remarks>
    private bool Matches(IUser user, string storedHash, string normalized)
        => IsLegacySha256(storedHash)
            ? CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(storedHash),
                Encoding.UTF8.GetBytes(hasher.Hash(normalized)))
            : passwords.Verify(user, storedHash, normalized);

    // A hex SHA-256 is exactly 64 hex characters; the PBKDF2 form is base64 and longer.
    private static bool IsLegacySha256(string storedHash)
        => storedHash.Length == 64 && storedHash.All(Uri.IsHexDigit);

    private Task<bool> RemoveEnrollmentAsync(Guid userId, CancellationToken cancellationToken)
        => transaction.InvokeAsync(async () =>
        {
            // Per-row removes (not a bulk ExecuteDelete) so the in-memory provider — used by tests and
            // the kiosk — keeps working. See the executedelete-breaks-inmemory-provider note.
            var codes = await backupCodes.ListByUserAsync(userId, cancellationToken);
            foreach (var backup in codes)
            {
                await backup.RemoveAsync(cancellationToken);
            }

            var enrollment = await enrollments.FindByUserAsync(userId, cancellationToken);
            if (enrollment is not null)
            {
                await enrollment.RemoveAsync(cancellationToken);
            }

            return enrollment is not null;
        }, cancellationToken);

    private LoginResult Issue(IUser user)
    {
        var issued = tokenIssuer.Issue(user);
        return new LoginResult(user, issued.Token, issued.ExpiresAt);
    }

    /// <summary>
    /// Mints a fresh batch of backup codes, returning the display forms (shown once) and the hashes
    /// to store.
    /// </summary>
    /// <remarks>
    /// Hashed through <see cref="IPasswordService"/> — PBKDF2 with a per-code salt — rather than the
    /// unkeyed <see cref="ISecretHasher"/> used for Proxytrace's own 256-bit CSPRNG secrets. A code
    /// is ~50 bits, sized to be typed by a human, which is well within reach of a GPU rig against a
    /// single-round unsalted hash; and because that hash was the same function for everyone, one
    /// dump could be attacked against every user's codes at once.
    /// </remarks>
    private (IReadOnlyList<string> Display, IReadOnlyList<string> Hashes) GenerateBackupCodes(IUser user)
    {
        var display = new List<string>(BackupCodeCount);
        var hashes = new List<string>(BackupCodeCount);
        for (var i = 0; i < BackupCodeCount; i++)
        {
            var raw = RandomCode(BackupCodeChars);
            display.Add($"{raw[..5]}-{raw[5..]}");
            hashes.Add(passwords.Hash(user, raw));
        }
        return (display, hashes);
    }

    private static string RandomCode(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(chars);
    }

    private static string NormalizeBackupCode(string code)
        => new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
