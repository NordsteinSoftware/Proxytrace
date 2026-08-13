using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.MfaBackupCode.Internal;

internal record MfaBackupCode : DomainEntity<IMfaBackupCode>, IMfaBackupCode
{
    public IUser User { get; }
    public string CodeHash { get; }
    public DateTimeOffset? ConsumedAt { get; private init; }

    public MfaBackupCode(
        IUser user,
        string codeHash,
        IRepository<IMfaBackupCode> repository) : base(repository)
    {
        User = user;
        CodeHash = codeHash;
    }

    public MfaBackupCode(
        IUser user,
        string codeHash,
        DateTimeOffset? consumedAt,
        IDomainEntityData existing,
        IRepository<IMfaBackupCode> repository) : base(existing, repository)
    {
        User = user;
        CodeHash = codeHash;
        ConsumedAt = consumedAt;
    }

    public Task<IMfaBackupCode> MarkConsumedAsync(CancellationToken cancellationToken = default)
    {
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException($"MFA backup code {Id} has already been consumed.");
        }
        return ApplyAsync(this with { ConsumedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        yield return Validation.NotNullOrWhiteSpace(CodeHash);

        foreach (var result in User.Validate(validationContext))
        {
            yield return result;
        }
    }
}
