using System.Collections.Concurrent;
using System.Security.Cryptography;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Internal;

internal sealed class StreamTicketService : IStreamTicketService
{
    private const int TokenBytes = 32;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Entry> tickets = new(StringComparer.Ordinal);

    /// <summary>
    /// Issue.
    /// </summary>
    public StreamTicket Issue(IUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        PruneExpired();

        var token = GenerateToken();
        var expiresAt = DateTimeOffset.UtcNow + Ttl;
        tickets[token] = new Entry(user.Id, expiresAt);
        return new StreamTicket(token, expiresAt);
    }

    /// <summary>
    /// Consume.
    /// </summary>
    public Guid? Consume(string token)
    {
        if (string.IsNullOrEmpty(token) || !tickets.TryRemove(token, out var entry))
        {
            return null;
        }

        return entry.ExpiresAt > DateTimeOffset.UtcNow ? entry.UserId : null;
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, entry) in tickets)
        {
            if (entry.ExpiresAt <= now)
            {
                tickets.TryRemove(token, out _);
            }
        }
    }

    private static string GenerateToken()
    {
        var bytes = new byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private sealed record Entry(Guid UserId, DateTimeOffset ExpiresAt);
}
