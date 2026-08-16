using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.PasswordResetToken.Internal;

internal record PasswordResetToken : DomainEntity<IPasswordResetToken>, IPasswordResetToken
{
    /// <summary>
    /// Gets the user.
    /// </summary>
    public IUser User { get; }
    /// <summary>
    /// Gets the token hash.
    /// </summary>
    public string TokenHash { get; }
    /// <summary>
    /// Gets the expires at.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }
    /// <summary>
    /// Gets or sets the consumed at.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordResetToken"/> class.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordResetToken"/> class.
    /// </summary>
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

    /// <summary>
    /// Mark consumed asynchronously.
    /// </summary>
    public Task<IPasswordResetToken> MarkConsumedAsync(CancellationToken cancellationToken = default)
    {
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException($"Password reset token {Id} has already been consumed.");
        }
        return ApplyAsync(this with { ConsumedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    /// <summary>
    /// Validates.
    /// </summary>
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
