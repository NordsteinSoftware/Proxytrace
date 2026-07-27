namespace Proxytrace.Common.Random;

/// <summary>
/// Deterministic pseudo-random values for <b>generating test and demo data</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never use this for anything security-relevant.</b> The registered implementation is
/// <c>SeededRandom</c> — a <see cref="System.Random"/> with a <b>fixed seed</b>, so its output is
/// identical on every run and in every process. That is exactly what test-data generators and the
/// demo seeder want (reproducible fixtures) and exactly what a credential must never be.
/// </para>
/// <para>
/// Secrets — API keys, invite and password-reset tokens, TOTP secrets, MFA backup codes, stream
/// tickets — use <see cref="System.Security.Cryptography.RandomNumberGenerator"/> directly at their
/// point of use. <c>SeededRandomIsNotUsedForSecretsTests</c> enforces the separation: it fails if
/// any production type outside the generator surface takes an <see cref="IRandom"/> dependency.
/// </para>
/// </remarks>
public interface IRandom
{
    bool Bool();
    Guid Guid();
    string String();
    string UniqueString();
    string Email();
    Uri Uri();
    int Int(int? min = null, int? max = null);
    long Long(long? min = null, long? max = null);
    double Double(double? min = null, double? max = null);
    decimal Decimal(decimal? min = null, decimal? max = null);
    T Any<T>(IReadOnlyCollection<T> options);
    T Enum<T>() where T : struct, Enum;
    TimeSpan TimeSpan(TimeSpan? min = null, TimeSpan? max = null);
    DateTimeOffset DateTimeOffset(DateTimeOffset? min = null, DateTimeOffset? max = null);
}