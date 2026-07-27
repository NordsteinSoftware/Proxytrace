namespace Proxytrace.Domain.Security;

/// <summary>
/// Deterministic blind index for a secret that is <b>not</b> guaranteed to be high-entropy — today,
/// the operator-entered upstream provider API key.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISecretHasher"/> is a plain unkeyed SHA-256, which is safe only because the secrets it
/// covers are 256-bit CSPRNG values that a dump cannot reverse. The upstream provider key is not one
/// of those: an operator types it in, and self-hosted OpenAI-compatible backends conventionally use
/// values like <c>EMPTY</c>, <c>ollama</c>, or <c>sk-1234</c>. An unkeyed index over those falls to a
/// wordlist in seconds, which would undo the column encryption sitting right beside it.
/// </para>
/// <para>
/// The index is therefore an <b>HMAC under a key that never leaves the deployment</b> (see
/// <c>docs/security.md</c>). A dump of the database alone no longer yields the key, because it does
/// not contain the HMAC key.
/// </para>
/// <para>
/// Values are prefixed with a scheme marker so the storage layer can tell a keyed index from a
/// legacy unkeyed one and upgrade rows in place.
/// </para>
/// </remarks>
public interface ISecretIndexer
{
    /// <summary>
    /// Returns the blind index for <paramref name="value"/>, prefixed with its scheme marker.
    /// </summary>
    string Index(string value);

    /// <summary>
    /// Returns the pre-keying unkeyed index for <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// Only for matching rows that predate the keyed scheme, so a lookup keeps working until the
    /// backfill has upgraded them. Never write this for a new row.
    /// </remarks>
    string LegacyIndex(string value);

    /// <summary>
    /// Whether <see cref="Index"/> is actually keyed.
    /// </summary>
    /// <remarks>
    /// False when no persisted key is available (no <c>PROXYTRACE_DATA_DIR</c> — Development and the
    /// test harnesses). The index then falls back to the legacy unkeyed form rather than to an
    /// ephemeral key, because an ephemeral key would silently stop every existing provider from
    /// authenticating on the next restart. The backfill uses this to decide whether upgrading rows
    /// is safe.
    /// </remarks>
    bool IsKeyed { get; }
}
