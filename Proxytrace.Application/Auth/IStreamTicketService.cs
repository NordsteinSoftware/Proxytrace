using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth;

/// <summary>
/// Issues and validates short-lived, single-use tickets that authenticate an SSE
/// (EventSource) connection without putting the long-lived session JWT in the query string.
/// </summary>
public interface IStreamTicketService
{
    /// <summary>
    /// Mints a fresh single-use ticket for the given user.
    /// </summary>
    StreamTicket Issue(IUser user);

    /// <summary>
    /// Redeems a ticket, returning the user id it was issued for, or <see langword="null"/>
    /// when the ticket is unknown, already consumed, or expired. A ticket validates at most once.
    /// </summary>
    Guid? Consume(string token);
}

/// <summary>
/// Represents a stream ticket.
/// </summary>
public sealed record StreamTicket(string Token, DateTimeOffset ExpiresAt);
