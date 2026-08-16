using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.ApiKey.Internal;

internal record ApiKey : DomainEntity<IApiKey>, IApiKey
{
    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the key hash.
    /// </summary>
    public string KeyHash { get; }
    /// <summary>
    /// Gets the key prefix.
    /// </summary>
    public string KeyPrefix { get; }
    /// <summary>
    /// Gets the project.
    /// </summary>
    public IProject Project { get; }
    /// <summary>
    /// Gets the provider.
    /// </summary>
    public IModelProvider Provider { get; }
    /// <summary>
    /// Gets the scopes.
    /// </summary>
    public ApiKeyScopes Scopes { get; }
    /// <summary>
    /// Gets the owner.
    /// </summary>
    public IUser Owner { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKey"/> class.
    /// </summary>
    public ApiKey(
        string name,
        string keyHash,
        string keyPrefix,
        IProject project,
        IModelProvider provider,
        ApiKeyScopes scopes,
        IUser owner,
        IRepository<IApiKey> repository) : base(repository)
    {
        Name = name;
        KeyHash = keyHash;
        KeyPrefix = keyPrefix;
        Project = project;
        Provider = provider;
        Scopes = scopes;
        Owner = owner;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKey"/> class.
    /// </summary>
    public ApiKey(
        string name,
        string keyHash,
        string keyPrefix,
        IProject project,
        IModelProvider provider,
        ApiKeyScopes scopes,
        IUser owner,
        IDomainEntityData existing,
        IRepository<IApiKey> repository) : base(existing, repository)
    {
        Name = name;
        KeyHash = keyHash;
        KeyPrefix = keyPrefix;
        Project = project;
        Provider = provider;
        Scopes = scopes;
        Owner = owner;
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

        yield return Validation.NotNullOrWhiteSpace(Name);
        yield return Validation.NotNullOrWhiteSpace(KeyHash);
        yield return Validation.NotNull(Owner);

        if (Scopes == ApiKeyScopes.None)
        {
            yield return new ValidationResult("An API key must grant at least one scope.", [nameof(Scopes)]);
        }
    }
}
