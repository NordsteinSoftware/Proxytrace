using Microsoft.AspNetCore.DataProtection;
using Nordstein.Core.Common.Security;

namespace Proxytrace.Infrastructure.Security.Internal;

internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataProtectionSecretProtector"/> class.
    /// </summary>
    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("Proxytrace.Secrets.v1");
    }

    /// <summary>
    /// Protect.
    /// </summary>
    public string Protect(string plaintext) => protector.Protect(plaintext);

    /// <summary>
    /// Unprotect.
    /// </summary>
    public string Unprotect(string ciphertext) => protector.Unprotect(ciphertext);
}
