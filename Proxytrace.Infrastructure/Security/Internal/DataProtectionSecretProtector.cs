using Microsoft.AspNetCore.DataProtection;
using Nordstein.Core.Common.Security;

namespace Proxytrace.Infrastructure.Security.Internal;

internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("Proxytrace.Secrets.v1");
    }

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => protector.Unprotect(ciphertext);
}
