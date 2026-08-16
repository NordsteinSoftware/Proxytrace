using Nordstein.Core.Common.Random;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Domain;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.ApiKey.Internal;

internal class ApiKeyGenerator : DomainEntityGenerator<IApiKey>
{
    private readonly IApiKey.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;
    private readonly IDomainEntityGenerator<IModelProvider> providerGenerator;
    private readonly IDomainEntityGenerator<IUser> userGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyGenerator"/> class.
    /// </summary>
    public ApiKeyGenerator(
        IApiKey.CreateNew factory,
        IRepository<IApiKey> repository,
        IDomainEntityGenerator<IProject> projectGenerator,
        IDomainEntityGenerator<IModelProvider> providerGenerator,
        IDomainEntityGenerator<IUser> userGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
        this.providerGenerator = providerGenerator;
        this.userGenerator = userGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IApiKey> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var project = await projectGenerator.GetOrCreateAsync(cancellationToken);
        var provider = await providerGenerator.GetOrCreateAsync(cancellationToken);
        var owner = await userGenerator.GetOrCreateAsync(cancellationToken);
        // Generated keys carry their stored shape: the hash of a fresh raw key plus a display prefix.
        var raw = $"proxytrace-{random.UniqueString()}";
        return factory(
            name: random.String(),
            keyHash: Sha256.HexHash(raw),
            keyPrefix: raw.Length <= 16 ? raw : raw[..16],
            project: project,
            provider: provider,
            scopes: ApiKeyScopes.Ingestion | ApiKeyScopes.McpRead | ApiKeyScopes.McpWrite,
            owner: owner);
    }
}
