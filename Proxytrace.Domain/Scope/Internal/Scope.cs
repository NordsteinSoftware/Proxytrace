using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Scope.Internal;

internal record Scope : DomainEntity<IScope>, IScope
{
    /// <summary>
    /// Gets the external key.
    /// </summary>
    public string ExternalKey { get; }
    /// <summary>
    /// Gets the project id.
    /// </summary>
    public Guid ProjectId { get; }
    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string? DisplayName { get; private init; }
    /// <summary>
    /// Gets the description.
    /// </summary>
    public string? Description { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scope"/> class.
    /// </summary>
    public Scope(
        string externalKey,
        Guid projectId,
        string? displayName,
        string? description,
        IRepository<IScope> repository) : base(repository)
    {
        ExternalKey = externalKey;
        ProjectId = projectId;
        DisplayName = displayName;
        Description = description;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scope"/> class.
    /// </summary>
    public Scope(
        string externalKey,
        Guid projectId,
        string? displayName,
        string? description,
        IDomainEntityData existing,
        IRepository<IScope> repository) : base(existing, repository)
    {
        ExternalKey = externalKey;
        ProjectId = projectId;
        DisplayName = displayName;
        Description = description;
    }

    /// <summary>
    /// Change details.
    /// </summary>
    public Task<IScope> ChangeDetails(
        string? displayName,
        string? description,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            this with { DisplayName = Blank(displayName), Description = Blank(description) },
            cancellationToken);

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotNullOrWhiteSpace(ExternalKey);
        yield return Validation.True(ScopeKey.IsCanonical(ExternalKey), nameof(ExternalKey));
        yield return Validation.NotDefault(ProjectId);
        yield return Validation.MaxLength(DisplayName, IScope.MaxDisplayNameLength);
        yield return Validation.MaxLength(Description, IScope.MaxDescriptionLength);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
