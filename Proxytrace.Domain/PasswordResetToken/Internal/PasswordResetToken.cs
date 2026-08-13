using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.PasswordResetToken.Internal;

internal record PasswordResetToken : DomainEntity<IPasswordResetToken>, IPasswordResetToken
{
    public IUser User { get; }
    public string TokenHash { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset? ConsumedAt { get; private init; }

    public PasswordResetToken(
        IUser user,
        string tokenHash,
        DateTimeOffset expiresAt,
        IRepository<IPasswordResetToken> repository) : base(repository)
    {
        User = user;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public PasswordResetToken(
        IUser user,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset? consumedAt,
        IDomainEntityData existing,
        IRepository<IPasswordResetToken> repository) : base(existing, repository)
    {
        User = user;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
    }

    public Task<IPasswordResetToken> MarkConsumedAsync(CancellationToken cancellationToken = default)
    {
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException($"Password reset token {Id} has already been consumed.");
        }
        return ApplyAsync(this with { ConsumedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        yield return Validation.NotNullOrWhiteSpace(TokenHash);
        yield return Validation.NotBefore(ExpiresAt, CreatedAt);

        foreach (var result in User.Validate(validationContext))
        {
            yield return result;
        }
    }
}
