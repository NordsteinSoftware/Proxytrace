using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Proxytrace.Common.Security;
using Proxytrace.Domain.Security;

namespace Proxytrace.Infrastructure.Security.Internal;

/// <summary>
/// <see cref="ISecretIndexer"/> over HMAC-SHA256, keyed by <see cref="BlindIndexKey"/>.
/// </summary>
internal sealed class HmacSecretIndexer : ISecretIndexer
{
    private readonly BlindIndexKey key;

    public HmacSecretIndexer(BlindIndexKey key, ILogger<HmacSecretIndexer> logger)
    {
        this.key = key;

        if (!key.IsAvailable)
        {
            logger.LogWarning(
                "No persisted blind-index key is available, so upstream provider API keys are indexed "
                + "with an unkeyed hash. Set {Variable} to a persistent, writable directory so the key "
                + "can be stored; see docs/security.md.",
                SecretProtectionModule.DataDirectoryVariable);
        }
    }

    public bool IsKeyed => key.IsAvailable;

    public string Index(string value)
        => key.Material is { } material
            ? SecretIndexScheme.KeyedPrefix + Convert.ToHexString(
                HMACSHA256.HashData(material, Encoding.UTF8.GetBytes(value))).ToLowerInvariant()
            : LegacyIndex(value);

    public string LegacyIndex(string value) => Sha256.HexHash(value);
}
