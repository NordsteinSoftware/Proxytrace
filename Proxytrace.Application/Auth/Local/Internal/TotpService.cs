using OtpNet;

namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class TotpService : ITotpService
{
    private const string Issuer = "Proxytrace";

    // 20 bytes = 160 bits, the RFC 6238 / RFC 4226 recommended secret size for HMAC-SHA1.
    private const int SecretBytes = 20;

    // Accept the immediately adjacent steps too, so a client whose clock is up to one period off
    // (or who types the code as it rolls over) still authenticates.
    private static readonly VerificationWindow Window = new(previous: 1, future: 1);

    /// <summary>
    /// Generates the secret.
    /// </summary>
    public string GenerateSecret()
        => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(SecretBytes));

    /// <summary>
    /// Builds the otp auth uri.
    /// </summary>
    public string BuildOtpAuthUri(string email, string secret)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{email}");
        var issuer = Uri.EscapeDataString(Issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    /// <summary>
    /// Tries to the verify.
    /// </summary>
    public bool TryVerify(string secret, string code, long? lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Base32Encoding.ToBytes(secret);
        }
        catch (ArgumentException)
        {
            // A corrupt/undecryptable secret (e.g. empty after a key-ring loss) — never authenticates.
            return false;
        }

        var totp = new Totp(key);
        if (!totp.VerifyTotp(code.Trim(), out matchedStep, Window))
        {
            return false;
        }

        // Replay guard: a code from a step already consumed cannot be reused within its window.
        return !lastUsedStep.HasValue || matchedStep > lastUsedStep.Value;
    }
}
