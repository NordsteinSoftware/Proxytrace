using System.Security.Cryptography;

namespace Proxytrace.Api.Auth;

internal sealed class SigningKeyProvider : ISigningKeyProvider
{
    private const int MinKeyLength = 32;
    private const int GeneratedKeyByteLength = 64;

    private readonly ISigningKeyStore store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SigningKeyProvider"/> class.
    /// </summary>
    public SigningKeyProvider(ISigningKeyStore store)
    {
        this.store = store;
    }

    /// <summary>
    /// Returns the JWT signing key to use for local-mode authentication. Prefers
    /// <paramref name="configured"/> when set (must be at least 32 characters), then falls back to a
    /// previously generated key from the store, and finally generates and persists a new key when none
    /// exists.
    /// </summary>
    public string EnsureSigningKey(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < MinKeyLength)
            {
                throw new InvalidOperationException(
                    $"Configured local signing key must be at least {MinKeyLength} characters.");
            }

            return configured;
        }

        // Reuse a previously generated key so sessions survive restarts; the appsettings-based
        // store also resurfaces it through configuration, but the data-directory store (used in
        // containers) is only reachable this way.
        var stored = store.Load();
        if (!string.IsNullOrWhiteSpace(stored) && stored.Length >= MinKeyLength)
            return stored;

        var generated = GenerateKey();
        store.Persist(generated);
        return generated;
    }

    private static string GenerateKey()
    {
        var bytes = new byte[GeneratedKeyByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
