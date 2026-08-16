using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// Authenticates a user by email and password and issues a session token, handling the MFA challenge when the account requires a second factor.
/// </summary>
public interface ILoginService
{
    /// <summary>
    /// Verifies credentials. Returns <see langword="null"/> when the email/password is invalid;
    /// otherwise a <see cref="LoginOutcome"/> that is either an issued session
    /// (<see cref="LoginSucceeded"/>) or — when the account has confirmed TOTP MFA — a second-factor
    /// challenge (<see cref="MfaRequired"/>). No session is issued until the second factor is verified.
    /// </summary>
    Task<LoginOutcome?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}

/// <summary>A successfully issued session: the authenticated user plus their session token.</summary>
public sealed record LoginResult(IUser User, string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// The result of passing the first authentication factor (password, or a redeemed reset token):
/// either the session is issued outright, or a second factor is still required.
/// </summary>
public abstract record LoginOutcome;

/// <summary>The first factor was sufficient (no MFA) — the session is issued.</summary>
public sealed record LoginSucceeded(IUser User, string Token, DateTimeOffset ExpiresAt) : LoginOutcome;

/// <summary>
/// The first factor passed but the account has confirmed TOTP MFA. The caller must complete the
/// challenge (POST /api/auth/mfa/verify) with <paramref name="ChallengeToken"/> to obtain a session.
/// <paramref name="User"/> is for the caller's audit context only — it is never sent to the client.
/// </summary>
public sealed record MfaRequired(IUser User, string ChallengeToken, DateTimeOffset ChallengeExpiresAt) : LoginOutcome;
