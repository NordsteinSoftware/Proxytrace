using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Proxytrace.Infrastructure.Security.Internal;

/// <summary>
/// The HMAC key backing <see cref="HmacSecretIndexer"/>, persisted beside the Data Protection key
/// ring so every host that reads or writes provider credentials derives the same index.
/// </summary>
/// <remarks>
/// <para>
/// Kept in its own file rather than derived from the Data Protection ring: Data Protection
/// deliberately exposes no stable raw key material (its whole API is protect/unprotect, and its
/// output is non-deterministic), so there is nothing there to HMAC with.
/// </para>
/// <para>
/// The file is created once, with owner-only permissions, and never rotated automatically —
/// rotating it would invalidate every stored index at once and stop every provider from
/// authenticating until a re-index. A deliberate rotation therefore means: delete the file, restart,
/// and let the backfill re-index from the decrypted keys.
/// </para>
/// <para>
/// When no data directory is configured the key is <b>absent</b>, not ephemeral. A per-process
/// random key would produce indexes that stop matching on the next restart, silently breaking
/// upstream-key authentication for every provider; falling back to the unkeyed index instead keeps
/// Development and the test harnesses working exactly as before.
/// </para>
/// </remarks>
internal sealed class BlindIndexKey
{
    private const string KeyFileName = "blind-index.key";
    private const int KeyLengthBytes = 32;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlindIndexKey"/> class.
    /// </summary>
    public BlindIndexKey(KeyRingLocation location, ILogger<BlindIndexKey> logger)
    {
        if (location.KeyRingPath is not { } keyRingPath)
        {
            Material = null;
            return;
        }

        try
        {
            Material = LoadOrCreate(Path.Combine(keyRingPath, KeyFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Never fail host boot over this: falling back to the unkeyed index keeps existing
            // providers authenticating, which is strictly better than refusing to start. It is a
            // real misconfiguration though, so it is loud.
            logger.LogCritical(ex,
                "Could not read or create the blind-index key at {Path}. Upstream provider API keys "
                + "will be indexed with an unkeyed hash until this is fixed; a database dump would "
                + "then be enough to recover a weak provider key by wordlist. See docs/security.md.",
                Path.Combine(keyRingPath, KeyFileName));
            Material = null;
        }
    }

    /// <summary>The key bytes, or <see langword="null"/> when no persisted key is available.</summary>
    public byte[]? Material { get; }

    /// <summary>
    /// Gets the is available.
    /// </summary>
    public bool IsAvailable => Material is not null;

    private static byte[] LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length == KeyLengthBytes)
            {
                return existing;
            }

            // A truncated or corrupt key must not be silently replaced — regenerating would
            // invalidate every stored index and break provider authentication with no explanation.
            throw new CryptographicException(
                $"The blind-index key at {path} is {existing.Length} bytes; expected {KeyLengthBytes}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var material = RandomNumberGenerator.GetBytes(KeyLengthBytes);

        // Write through a temporary file and move it into place, so a crash mid-write cannot leave a
        // half-written key that the branch above would then refuse to load on every boot.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, material);
        RestrictToOwner(temporary);
        File.Move(temporary, path, overwrite: false);
        return material;
    }

    private static void RestrictToOwner(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
