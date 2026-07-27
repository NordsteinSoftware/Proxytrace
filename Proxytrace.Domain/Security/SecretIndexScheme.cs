namespace Proxytrace.Domain.Security;

/// <summary>
/// Scheme markers stored inline in a blind-index column.
/// </summary>
/// <remarks>
/// A hex SHA-256 and a hex HMAC-SHA256 are both 64 characters, so length cannot tell the pre-keying
/// index from the keyed one. The scheme is therefore written into the value itself, which is what
/// lets the lookup match both while rows are being upgraded and lets the backfill find the ones that
/// still need upgrading. Shared here because both the infrastructure implementation and the storage
/// backfill need the same literal.
/// </remarks>
public static class SecretIndexScheme
{
    /// <summary>Prefix identifying a keyed (HMAC-SHA256) blind index.</summary>
    public const string KeyedPrefix = "hmac1:";
}
