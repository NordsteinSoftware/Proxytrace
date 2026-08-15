using Proxytrace.Domain.User;

namespace Proxytrace.Domain.UserTotpEnrollment;

/// <summary>
/// A user's TOTP (RFC 6238) second-factor enrollment. Created in a <em>pending</em> state when the
/// user starts setup and promoted to <em>confirmed</em> once they prove possession of the
/// authenticator by entering a valid code. A confirmed enrollment is what makes a login require the
/// second factor; a pending one is inert (a stale setup the user never finished).
/// </summary>
public interface IUserTotpEnrollment : IDomainEntity<IUserTotpEnrollment>
{
    /// <summary>The user this enrollment belongs to (one enrollment per user).</summary>
    IUser User { get; }

    /// <summary>
    /// The Base32-encoded TOTP shared secret. Replayable (the server must reproduce it to verify
    /// codes), so it is <strong>encrypted at rest</strong> via <c>ISecretProtector</c> in the storage
    /// mapper — never hashed.
    /// </summary>
    string Secret { get; }

    /// <summary>When the user confirmed the enrollment, or <see langword="null"/> while still pending.</summary>
    DateTimeOffset? ConfirmedAt { get; }

    /// <summary>
    /// The TOTP time-step of the most recently accepted code. Guards against replay: a code whose
    /// matched step is ≤ this value is rejected even though it is still inside its validity window.
    /// <see langword="null"/> until the first code is accepted.
    /// </summary>
    long? LastUsedStep { get; }

    /// <summary>Whether the enrollment has been confirmed and is therefore enforced at login.</summary>
    bool IsConfirmed => ConfirmedAt is not null;

    /// <summary>Promotes a pending enrollment to confirmed and records the consumed time-step. Persists.</summary>
    Task<IUserTotpEnrollment> Confirm(long usedStep, CancellationToken cancellationToken = default);

    /// <summary>Records the time-step of an accepted code (replay guard) on a confirmed enrollment. Persists.</summary>
    Task<IUserTotpEnrollment> RecordUsedStep(long step, CancellationToken cancellationToken = default);

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IUserTotpEnrollment CreateNew(IUser user, string secret);
    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IUserTotpEnrollment CreateExisting(IUser user, string secret, DateTimeOffset? confirmedAt, long? lastUsedStep, IDomainEntityData existing);
}
