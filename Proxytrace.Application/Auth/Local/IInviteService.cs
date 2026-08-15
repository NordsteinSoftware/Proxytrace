using Proxytrace.Domain.Invite;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local;

/// <summary>
/// A freshly created invite together with its raw redemption token. Only the token's hash is
/// persisted, so the raw value is available exactly once — here — to build the invite link.
/// </summary>
public sealed record InviteCreated(IInvite Invite, string RawToken);

/// <summary>
/// Service that provides invite functionality.
/// </summary>
public interface IInviteService
{
    Task<InviteCreated> CreateAsync(
        string email,
        UserRole role,
        IUser invitedBy,
        CancellationToken cancellationToken = default);
    
    Task<IInvite?> GetByTokenAsync(
        string token,
        CancellationToken cancellationToken = default);
    
    Task<IUser?> ConsumeAsync(
        string token, 
        string password, 
        CancellationToken cancellationToken = default);
}
