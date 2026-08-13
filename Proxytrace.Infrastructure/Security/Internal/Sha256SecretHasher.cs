using Nordstein.Core.Common.Security;

namespace Proxytrace.Infrastructure.Security.Internal;

internal sealed class Sha256SecretHasher : ISecretHasher
{
    public string Hash(string value) => Sha256.HexHash(value);
}
