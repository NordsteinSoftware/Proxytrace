using System.Collections.Concurrent;
using System.Security.Cryptography;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Internal;

internal sealed class MfaChallengeService : IMfaChallengeService
{
    private const int TokenBytes = 32;
    private const int MaxFailures = 5;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> challenges = new(StringComparer.Ordinal);

    /// <summary>
    /// Issue.
    /// </summary>
    public MfaChallenge Issue(IUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        PruneExpired();

        var token = GenerateToken();
        var expiresAt = DateTimeOffset.UtcNow + Ttl;
        challenges[token] = new Entry(user.Id, expiresAt);
        return new MfaChallenge(token, expiresAt);
    }

    /// <summary>
    /// Peek.
    /// </summary>
    public Guid? Peek(string token)
    {
        if (string.IsNullOrEmpty(token) || !challenges.TryGetValue(token, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            challenges.TryRemove(token, out _);
            return null;
        }

        return entry.UserId;
    }

    /// <summary>
    /// Consume.
    /// </summary>
    public void Consume(string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            challenges.TryRemove(token, out _);
        }
    }

    /// <summary>
    /// Registers the failure.
    /// </summary>
    public bool RegisterFailure(string token)
    {
        if (string.IsNullOrEmpty(token) || !challenges.TryGetValue(token, out var entry))
        {
            return false;
        }

        if (Interlocked.Increment(ref entry.Failures) >= MaxFailures)
        {
            challenges.TryRemove(token, out _);
            return false;
        }

        return true;
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, entry) in challenges)
        {
            if (entry.ExpiresAt <= now)
            {
                challenges.TryRemove(token, out _);
            }
        }
    }

    private static string GenerateToken()
    {
        var bytes = new byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private sealed class Entry(Guid userId, DateTimeOffset expiresAt)
    {
        /// <summary>
        /// Gets the user id.
        /// </summary>
        public Guid UserId { get; } = userId;
        /// <summary>
        /// Gets the expires at.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        /// <summary>
        /// The failures.
        /// </summary>
        public int Failures;
    }
}
