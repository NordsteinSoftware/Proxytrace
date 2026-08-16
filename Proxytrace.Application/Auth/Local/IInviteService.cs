using Proxytrace.Domain.Invite;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// A freshly created invite together with its raw redemption token. Only the token's hash is
/// persisted, so the raw value is available exactly once — here — to build the invite link.
/// </summary>
public sealed record InviteCreated(IInvite Invite, string RawToken);

/// <summary>
/// Manages email invitations: creates invite records with hashed tokens, validates tokens on redemption, and creates the invited user on first use.
/// </summary>
public interface IInviteService
{
    /// <summary>
    /// Creates an invite for the given email and returns the entity with its raw (un-hashed) token for building the invite link.
    /// </summary>
    Task<InviteCreated> CreateAsync(
        string email,
        UserRole role,
        IUser invitedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active invite matching the given raw token, or null when the token is unknown or expired.
    /// </summary>
    Task<IInvite?> GetByTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems the invite token, creates the user account with the supplied password, and returns the new user; null when the token is invalid.
    /// </summary>
    Task<IUser?> ConsumeAsync(
        string token,
        string password,
        CancellationToken cancellationToken = default);
}
