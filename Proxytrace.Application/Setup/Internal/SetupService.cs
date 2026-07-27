using Proxytrace.Common.Async;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Application.Pricing;
using Proxytrace.Domain;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;

namespace Proxytrace.Application.Setup.Internal;

internal class SetupService : ISetupService
{
    private readonly IRepository<IModelProvider> providers;
    private readonly IModelRepository models;
    private readonly IModelEndpointRepository endpoints;
    private readonly IProjectRepository projects;
    private readonly IUserRepository users;
    private readonly ICurrentUserAccessor currentUser;
    private readonly IModelProvider.CreateNew createProvider;
    private readonly IModelEndpoint.CreateNew createEndpoint;
    private readonly IProject.CreateNew createProject;
    private readonly IUser.CreateNew createUser;
    private readonly IPasswordService passwords;
    private readonly IAsyncLock asyncLock;

    /// <summary>Lock key serializing first-run setup against a concurrent submission of itself.</summary>
    private const string SetupLockKey = "license-quota:setup";
    private readonly ILocalTokenIssuer tokens;
    private readonly ITransaction transaction;
    private readonly ILicenseService license;
    private readonly IModelPriceRefresher priceRefresher;

    public SetupService(
        IRepository<IModelProvider> providers,
        IModelRepository models,
        IModelEndpointRepository endpoints,
        IProjectRepository projects,
        IUserRepository users,
        ICurrentUserAccessor currentUser,
        IModelProvider.CreateNew createProvider,
        IModelEndpoint.CreateNew createEndpoint,
        IProject.CreateNew createProject,
        IUser.CreateNew createUser,
        IPasswordService passwords,
        IAsyncLock asyncLock,
        ILocalTokenIssuer tokens,
        ITransaction transaction,
        ILicenseService license,
        IModelPriceRefresher priceRefresher)
    {
        this.providers = providers;
        this.models = models;
        this.endpoints = endpoints;
        this.projects = projects;
        this.users = users;
        this.currentUser = currentUser;
        this.createProvider = createProvider;
        this.createEndpoint = createEndpoint;
        this.createProject = createProject;
        this.createUser = createUser;
        this.passwords = passwords;
        this.asyncLock = asyncLock;
        this.tokens = tokens;
        this.transaction = transaction;
        this.license = license;
        this.priceRefresher = priceRefresher;
    }

    public async Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default)
    {
        var count = await users.CountAsync(cancellationToken);
#if DEBUG
        // The DEBUG-only back-door admin (docs/debug_api.md) is seeded on startup, before anyone
        // opens the app. It must not count as a completed first-run setup — otherwise a fresh dev
        // database goes straight to the login form and the onboarding wizard is never shown.
        if (count == 1
            && await users.FindByEmailAsync(DebugBackDoorAccount.Email, cancellationToken) is not null)
        {
            return false;
        }
#endif
        return count > 0;
    }

    public async Task<FirstAdminResult> CreateFirstAdminAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (await AnyUsersExistAsync(cancellationToken))
            throw new InvalidOperationException("Setup already completed: users exist.");

        var draft = createUser(email, externalSubject: null, passwordHash: "placeholder", role: UserRole.Admin);
        var hash = passwords.Hash(draft, password);
        var withHash = createUser(email, externalSubject: null, passwordHash: hash, role: UserRole.Admin);
        var saved = await withHash.AddAsync(cancellationToken);
        var issued = tokens.Issue(saved);
        return new FirstAdminResult(saved.Id, issued.Token, issued.ExpiresAt);
    }

    public async Task<SetupResult> CompleteAsync(SetupInput input, CancellationToken cancellationToken = default)
    {
        // Taken outside the transaction, and held for the whole of it: the "already completed" test
        // and the project-limit check below are both check-then-act against the same count, so two
        // concurrent setup submissions could each observe zero projects and each proceed.
        using IDisposable setupLock = await asyncLock.LockAsync(SetupLockKey, cancellationToken);

        IModelProvider? savedProvider = null;
        var result = await transaction.InvokeAsync(async () =>
        {
            var projectCount = await projects.CountAsync(cancellationToken);
            if (projectCount > 0)
                throw new InvalidOperationException("Setup has already been completed.");

            license.Ensure(LicenseLimit.MaxProjects, projectCount);

            var user = await currentUser.GetCurrentUserAsync(cancellationToken)
                ?? throw new InvalidOperationException("Setup requires an authenticated user.");

            var provider = await providers.AddAsync(
                createProvider(input.ProviderName, input.ProviderEndpoint, input.ProviderUpstreamApiKey, input.ProviderKind),
                cancellationToken);
            savedProvider = provider;

            IModel model = await models.GetOrCreateAsync(input.ModelName, cancellationToken);

            // Prices are resolved by the refresher below (and the periodic refresh) — never entered manually.
            var endpoint = await endpoints.AddAsync(
                createEndpoint(model, provider, inputTokenCost: null, outputTokenCost: null, cachedInputTokenCost: null),
                cancellationToken);

            // No Proxytrace API key is created here: clients keep their upstream provider key
            // and authenticate via the project-scoped proxy path. Keys can still be issued
            // later from the Providers page.
            var project = await projects.AddAsync(
                createProject(input.ProjectName, endpoint, [user]),
                cancellationToken);

            return new SetupResult(provider.Id, endpoint.Id, project.Id);
        });

        // Same as provider creation in settings: discover all models and load catalogue prices.
        // Best-effort — a discovery failure must not fail setup.
        if (savedProvider is not null)
            await priceRefresher.RefreshProviderAsync(savedProvider, cancellationToken);

        return result;
    }

    public Task<ProviderConnectionResult> TestProviderConnectionAsync(ProviderConnectionInput input, CancellationToken cancellationToken = default)
        => CreateProvider(input)
            .CreateClient()
            .VerifyConnectionAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListProviderModelsAsync(ProviderConnectionInput input, CancellationToken cancellationToken = default)
    {
        IModelProvider provider = CreateProvider(input);
        var client = provider.CreateClient();
        var availableModels = await client.GetModelsAsync(cancellationToken);
        return availableModels.Select(m => m.Model.Name).ToArray();
    }

    private IModelProvider CreateProvider(ProviderConnectionInput input)
        => createProvider(
            name: input.ProviderName,
            endpoint: input.ProviderEndpoint,
            apiKey: input.ProviderUpstreamApiKey,
            kind: input.ProviderKind);
}
