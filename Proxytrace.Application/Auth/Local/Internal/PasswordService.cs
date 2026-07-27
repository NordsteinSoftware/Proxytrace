using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local.Internal;

internal sealed class PasswordService : IPasswordService
{
    // Typed as object rather than IUser: the identity hasher never reads the user (it is a pure
    // PBKDF2 over the password with a salt embedded in the hash), and dropping the constraint lets
    // VerifyDummy spend the same cost without inventing a fake IUser. The hash format is unchanged,
    // so existing stored hashes keep verifying.
    private readonly PasswordHasher<object> hasher = new();

    private static readonly object Sentinel = new();

    // A hash of a random password nobody can supply, computed once per process and only when first
    // needed. Verifying against it costs the same PBKDF2 work as verifying a real user's hash.
    private static readonly Lazy<string> DummyHash = new(
        () => new PasswordHasher<object>().HashPassword(
            Sentinel,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public string Hash(IUser user, string password)
        => hasher.HashPassword(user, password);

    public bool Verify(IUser user, string hash, string password)
    {
        var result = hasher.VerifyHashedPassword(user, hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    public void VerifyDummy(string password)
    {
        // The result is deliberately unused — only the elapsed work matters. The call is not
        // elidable: VerifyHashedPassword allocates and runs the full key derivation.
        _ = hasher.VerifyHashedPassword(Sentinel, DummyHash.Value, password);
    }
}
