using Proxytrace.Domain.User;

namespace Proxytrace.Application.Auth.Local;

public interface IPasswordService
{
    string Hash(IUser user, string password);
    bool Verify(IUser user, string hash, string password);

    /// <summary>
    /// Runs a full password verification against a fixed, unknowable dummy hash and discards the
    /// result. Call this on paths that bail out <b>before</b> reaching <see cref="Verify"/> — above
    /// all "no such user" — so the work done, and therefore the response time, does not disclose
    /// whether the account exists.
    /// </summary>
    /// <remarks>
    /// Verification is PBKDF2 with a high iteration count, so skipping it is measurable from the
    /// outside: an unknown email returned in a fraction of the time a known one took, which is a
    /// user-enumeration oracle. Rate limiting raises the cost of exploiting that but does not
    /// close it.
    /// </remarks>
    void VerifyDummy(string password);
}
