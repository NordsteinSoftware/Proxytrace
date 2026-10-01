using System.Security.Cryptography;
using System.Text;

namespace Proxytrace.Domain.Scope;

/// <summary>
/// Deterministically derives a scope's <see cref="Guid"/> from (project, canonical key) — so
/// ingestion stamps traces without a lookup, the upsert is idempotent, and a client-facing key can
/// be resolved to an id without a query. Like <c>SessionIdDerivation</c> this is a plain identity
/// derivation, never a security check; the <c>"scope\0"</c> prefix keeps a scope and a session with
/// the same key in the same project from colliding on one id.
/// </summary>
public static class ScopeIdDerivation
{
    private static readonly byte[] DomainPrefix = "scope\0"u8.ToArray();

    /// <summary>
    /// Derives the id of the scope <paramref name="canonicalKey"/> (see <see cref="ScopeKey.Normalize"/>)
    /// in <paramref name="projectId"/>.
    /// </summary>
    public static Guid Derive(Guid projectId, string canonicalKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(canonicalKey);
        var input = new byte[DomainPrefix.Length + 16 + keyBytes.Length];
        DomainPrefix.CopyTo(input, 0);
        projectId.TryWriteBytes(input.AsSpan(DomainPrefix.Length));
        keyBytes.CopyTo(input, DomainPrefix.Length + 16);
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }
}
