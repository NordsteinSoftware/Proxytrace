using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Proxy.Internal;

namespace Proxytrace.Proxy.Tests;

[TestClass]
public sealed class ApiKeyResolverTests
{
    [TestMethod]
    [DataRow("configured")]
    [DataRow("unknown-project")]
    [DataRow("disabled")]
    [DataRow("missing-provider")]
    [DataRow("archived-provider")]
    public async Task ResolveAnonymousUpstreamAsync_RequiresAnActiveConfiguredProvider(string scenario)
    {
        var projects = Substitute.For<IProjectRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        providers.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IModelProvider?)null);
        projects.FindBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IProject?)null);
        var project = Substitute.For<IProject>();
        var provider = Provider();
        var providerId = Guid.NewGuid();
        provider.Endpoint.Returns(new Uri("https://agents.translogica.ai:8443/openai/v1"));
        provider.IsArchived.Returns(scenario == "archived-provider");
        if (scenario != "disabled")
            project.DefaultUpstreamProviderId.Returns(providerId);
        if (scenario != "unknown-project")
            projects.FindBySlugAsync("translogica", Arg.Any<CancellationToken>()).Returns(project);
        if (scenario != "missing-provider")
            providers.FindAsync(providerId, Arg.Any<CancellationToken>()).Returns(provider);
        var resolver = NewResolver(Substitute.For<IApiKeyRepository>(), providers, projects);

        var result = await resolver.ResolveAnonymousUpstreamAsync("translogica", CancellationToken.None);

        result.Should().Be(scenario == "configured" ? new Uri("https://agents.translogica.ai:8443") : null);
        _ = provider.DidNotReceive().ApiKey;
    }

    [TestMethod]
    public async Task ResolveAsync_EveryCall_HitsRepositoryAgain()
    {
        // No positive credential caching (#407): each request must observe the current stored
        // credentials so rotation and revocation take effect immediately.
        IApiKey apiKey = ProxytraceKey();
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("key", Arg.Any<CancellationToken>()).Returns(apiKey);

        var resolver = NewResolver(apiKeys, providers, projects);

        await resolver.ResolveAsync("key", projectSlug: null, CancellationToken.None);
        await resolver.ResolveAsync("key", projectSlug: null, CancellationToken.None);

        await apiKeys.Received(2).FindByKeyAsync("key", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveAsync_AfterRotation_ReturnsTheNewProviderSnapshot()
    {
        // The second resolve must surface what the repository returns *now*, not a snapshot from
        // the first resolve — this is the rotation-freshness guarantee at the resolver level.
        IApiKey before = ProxytraceKey();
        IApiKey after = ProxytraceKey();
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("key", Arg.Any<CancellationToken>()).Returns(before, after);

        var resolver = NewResolver(apiKeys, providers, projects);

        var first = await resolver.ResolveAsync("key", projectSlug: null, CancellationToken.None);
        var second = await resolver.ResolveAsync("key", projectSlug: null, CancellationToken.None);

        first.Should().NotBeNull();
        first.Provider.Should().BeSameAs(before.Provider);
        second.Should().NotBeNull();
        second.Provider.Should().BeSameAs(after.Provider);
    }

    [TestMethod]
    public async Task ResolveAsync_AfterRevocation_ReturnsNullImmediately()
    {
        IApiKey apiKey = ProxytraceKey();
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("key", Arg.Any<CancellationToken>()).Returns(apiKey, (IApiKey?)null);

        var resolver = NewResolver(apiKeys, providers, projects);

        (await resolver.ResolveAsync("key", projectSlug: null, CancellationToken.None)).Should().NotBeNull();
        (await resolver.ResolveAsync("key", projectSlug: null, CancellationToken.None)).Should().BeNull();
    }

    [TestMethod]
    public async Task ResolveAsync_UpstreamProviderKey_ResolvesProjectFromSlug()
    {
        var provider = Provider();
        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());

        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("upstream", Arg.Any<CancellationToken>()).Returns((IApiKey?)null);
        providers.FindByApiKeyAsync("upstream", Arg.Any<CancellationToken>()).Returns(provider);
        projects.FindBySlugAsync("my-project", Arg.Any<CancellationToken>()).Returns(project);

        var resolver = NewResolver(apiKeys, providers, projects);

        var result = await resolver.ResolveAsync("upstream", "my-project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Provider.Should().BeSameAs(provider);
        result.Project.Should().BeSameAs(project);
    }

    [TestMethod]
    public async Task ResolveAsync_UpstreamProviderKey_RejectsWhenSlugMissing()
    {
        var provider = Provider();

        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("upstream", Arg.Any<CancellationToken>()).Returns((IApiKey?)null);
        providers.FindByApiKeyAsync("upstream", Arg.Any<CancellationToken>()).Returns(provider);

        var resolver = NewResolver(apiKeys, providers, projects);

        (await resolver.ResolveAsync("upstream", projectSlug: null, CancellationToken.None)).Should().BeNull();
        await projects.DidNotReceive().FindBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveAsync_UpstreamProviderKey_RejectsWhenSlugUnknown()
    {
        var provider = Provider();

        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("upstream", Arg.Any<CancellationToken>()).Returns((IApiKey?)null);
        providers.FindByApiKeyAsync("upstream", Arg.Any<CancellationToken>()).Returns(provider);
        projects.FindBySlugAsync("ghost", Arg.Any<CancellationToken>()).Returns((IProject?)null);

        var resolver = NewResolver(apiKeys, providers, projects);

        (await resolver.ResolveAsync("upstream", "ghost", CancellationToken.None)).Should().BeNull();
    }

    [TestMethod]
    public async Task ResolveAsync_ProxytraceKey_RejectsWhenSlugMismatches()
    {
        IApiKey apiKey = ProxytraceKey("Showcase Project");
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("pt", Arg.Any<CancellationToken>()).Returns(apiKey);

        var resolver = NewResolver(apiKeys, providers, projects);

        (await resolver.ResolveAsync("pt", "other-project", CancellationToken.None)).Should().BeNull();
    }

    [TestMethod]
    public async Task ResolveAsync_ProxytraceKey_AcceptsMatchingSlug()
    {
        IApiKey apiKey = ProxytraceKey("Showcase Project");
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("pt", Arg.Any<CancellationToken>()).Returns(apiKey);

        var resolver = NewResolver(apiKeys, providers, projects);

        var result = await resolver.ResolveAsync("pt", "showcase-project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Project.Should().BeSameAs(apiKey.Project);
    }

    [TestMethod]
    public async Task ResolveAsync_ProxytraceKey_AcceptsMixedCaseSlug()
    {
        IApiKey apiKey = ProxytraceKey("Showcase Project");
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("pt", Arg.Any<CancellationToken>()).Returns(apiKey);

        var resolver = NewResolver(apiKeys, providers, projects);

        // The slug arrives from the URL path with its original casing ("Showcase-Project"); it must
        // still match the key's canonical lower-cased project slug rather than 401.
        var result = await resolver.ResolveAsync("pt", "Showcase-Project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Project.Should().BeSameAs(apiKey.Project);
    }

    [TestMethod]
    public async Task ResolveAsync_ProxytraceKeyWins_OnCollisionWithProviderKey()
    {
        IApiKey apiKey = ProxytraceKey();
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("colliding", Arg.Any<CancellationToken>()).Returns(apiKey);

        var resolver = NewResolver(apiKeys, providers, projects);

        var result = await resolver.ResolveAsync("colliding", projectSlug: null, CancellationToken.None);

        result.Should().NotBeNull();
        result.Provider.Should().BeSameAs(apiKey.Provider);
        await providers.DidNotReceive().FindByApiKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResolveAsync_ProxytraceKey_WithoutIngestionScope_Rejected()
    {
        // An MCP-only key (no Ingestion scope) must not authenticate at the ingestion proxy.
        IApiKey apiKey = ProxytraceKey(scopes: ApiKeyScopes.McpRead | ApiKeyScopes.McpWrite);
        var apiKeys = Substitute.For<IApiKeyRepository>();
        var providers = Substitute.For<IModelProviderRepository>();
        var projects = Substitute.For<IProjectRepository>();
        apiKeys.FindByKeyAsync("mcp-only", Arg.Any<CancellationToken>()).Returns(apiKey);

        var resolver = NewResolver(apiKeys, providers, projects);

        (await resolver.ResolveAsync("mcp-only", projectSlug: null, CancellationToken.None)).Should().BeNull();
    }

    private static ApiKeyResolver NewResolver(
        IApiKeyRepository apiKeys,
        IModelProviderRepository providers,
        IProjectRepository projects)
        => new(apiKeys, providers, projects);

    private static IApiKey ProxytraceKey(string projectName = "Some Project", ApiKeyScopes scopes = ApiKeyScopes.Ingestion)
    {
        var apiKey = Substitute.For<IApiKey>();
        var provider = Provider();
        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());
        project.Name.Returns(projectName);
        apiKey.Provider.Returns(provider);
        apiKey.Project.Returns(project);
        apiKey.Scopes.Returns(scopes);
        return apiKey;
    }

    private static IModelProvider Provider()
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Id.Returns(Guid.NewGuid());
        return provider;
    }
}
