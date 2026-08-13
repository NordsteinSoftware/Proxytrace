using System.Security.Cryptography;
using System.Text;

namespace Nordstein.Core.Common.Security;

/// <summary>
/// Hex-encoded SHA-256 — the canonical deterministic blind-index hash for verify-only secrets
/// (inbound API keys, invite tokens). One-way and key-ring-independent, so equality lookups over the
/// hash survive a lost <c>PROXYTRACE_DATA_DIR</c>. Suitable because those secrets are high-entropy
/// (256-bit CSPRNG): a database dump cannot reverse or forge them. Not for passwords.
/// </summary>
public static class Sha256
{
    /// <summary>Returns the upper-case hex-encoded SHA-256 of <paramref name="value"/> (64 chars).</summary>
    public static string HexHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
