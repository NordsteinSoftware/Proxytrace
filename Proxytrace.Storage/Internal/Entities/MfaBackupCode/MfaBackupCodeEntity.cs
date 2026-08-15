using Proxytrace.Domain.MfaBackupCode;

namespace Proxytrace.Storage.Internal.Entities.MfaBackupCode;

[StoredDomainEntity(typeof(IMfaBackupCode))]
internal record MfaBackupCodeEntity : Entity
{
    /// <summary>
    /// Gets or sets the user.
    /// </summary>
    public required Guid User { get; init; }

    /// <summary><see cref="IMfaBackupCode.CodeHash"/> — SHA-256 of the raw code (verify-only).</summary>
    public required string CodeHash { get; init; }

    /// <summary>
    /// Gets or sets the consumed at.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; init; }
}
