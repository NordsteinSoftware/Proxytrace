using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth;

/// <summary>
/// Issues and validates short-lived, single-use MFA challenge tickets. A ticket is minted once the
/// password step of a login (or a password reset) succeeds for an MFA-enabled account; presenting a
/// valid second factor against the ticket is what finally issues the session. Tickets live in memory
/// only — losing them on restart is harmless (the user simply re-enters their password).
/// </summary>
public interface IMfaChallengeService
{
    /// <summary>Mints a fresh challenge ticket for the given user.</summary>
    MfaChallenge Issue(IUser user);

    /// <summary>
    /// Returns the user id the ticket was issued for without consuming it, or <see langword="null"/>
    /// when the ticket is unknown or expired. Used to look up the account before verifying the code.
    /// </summary>
    Guid? Peek(string token);

    /// <summary>Invalidates a ticket. Call after a successful verification (single-use).</summary>
    void Consume(string token);

    /// <summary>
    /// Records a failed verification attempt against the ticket. Returns <see langword="true"/> while
    /// the ticket is still usable, or <see langword="false"/> once the attempt cap is exceeded (the
    /// ticket is then invalidated, forcing the user to restart from the password step).
    /// </summary>
    bool RegisterFailure(string token);
}

/// <summary>
/// Represents a mfa challenge.
/// </summary>
public sealed record MfaChallenge(string Token, DateTimeOffset ExpiresAt);
