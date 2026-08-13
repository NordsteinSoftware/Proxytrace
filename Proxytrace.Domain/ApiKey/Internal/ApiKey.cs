using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.ApiKey.Internal;

internal record ApiKey : DomainEntity<IApiKey>, IApiKey
{
    public string Name { get; }
    public string KeyHash { get; }
    public string KeyPrefix { get; }
    public IProject Project { get; }
    public IModelProvider Provider { get; }
    public ApiKeyScopes Scopes { get; }
    public IUser Owner { get; }

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
