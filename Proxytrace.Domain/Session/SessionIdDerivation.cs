using System.Security.Cryptography;
using System.Text;

namespace Proxytrace.Domain.Session;

/// <summary>
/// Deterministically derives a session's <see cref="Guid"/> from (project, external key) by hashing
/// them into a stable id — so the ingestion upsert is idempotent and traces can be stamped without a
/// lookup. The hash is a plain identity/correlation derivation (never a security or integrity check),
/// so its only requirement is a stable, well-distributed 128-bit output.
/// </summary>
public static class SessionIdDerivation
{
    public static Guid Derive(Guid projectId, string externalKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(externalKey);
        var input = new byte[16 + keyBytes.Length];
        projectId.TryWriteBytes(input);
        keyBytes.CopyTo(input, 16);
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string TruncateKey(string raw)
        => raw.Length <= ISession.MaxExternalKeyLength ? raw : raw[..ISession.MaxExternalKeyLength];
}
