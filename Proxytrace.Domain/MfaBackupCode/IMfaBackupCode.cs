using Proxytrace.Domain.User;

namespace Proxytrace.Domain.MfaBackupCode;

/// <summary>
/// A single-use recovery code that substitutes for a TOTP code when the user has lost their
/// authenticator device. Issued in a batch at MFA activation; the raw codes are shown once and only
/// their hashes are stored (verify-only, like a password-reset token). Each code is consumed
/// independently the first time it is used.
/// </summary>
public interface IMfaBackupCode : IDomainEntity<IMfaBackupCode>
{
    /// <summary>The user who owns this backup code.</summary>
    IUser User { get; }

    /// <summary>
    /// Salted, slow hash of the raw backup code (PBKDF2, via <c>IPasswordService</c>). Verify-only —
    /// the raw code is unrecoverable.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> the unkeyed SHA-256 used for Proxytrace-generated 256-bit secrets. A
    /// backup code is ~50 bits (10 characters over a 32-symbol alphabet), which is chosen to be
    /// typeable rather than unguessable, and one unsalted single-round hash per code means a single
    /// dump can be attacked against every user's codes at once on commodity GPU hardware. Per-code
    /// salting removes the shared work, and the iteration count removes the throughput.
    /// </remarks>
    string CodeHash { get; }

    /// <summary>When the code was redeemed, or <see langword="null"/> if still usable.</summary>
    DateTimeOffset? ConsumedAt { get; }

    bool IsConsumed => ConsumedAt is not null;

    /// <summary>Marks the code as redeemed and persists.</summary>
    Task<IMfaBackupCode> MarkConsumedAsync(CancellationToken cancellationToken = default);

    public delegate IMfaBackupCode CreateNew(IUser user, string codeHash);
    public delegate IMfaBackupCode CreateExisting(IUser user, string codeHash, DateTimeOffset? consumedAt, IDomainEntityData existing);
}
