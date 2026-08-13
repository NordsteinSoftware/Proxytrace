using AwesomeAssertions;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Proxytrace.Api.Auth.Mcp;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests.Mcp;

/// <summary>
/// End-to-end test of the MCP endpoint over real HTTP: a TestServer host wired exactly like production
/// (in-memory storage, the McpApiKey scheme, the "Mcp" policy, stateless Streamable HTTP) is driven by
/// the real MCP client. This is the regression guard for the per-request flow — that an MCP tool, run
/// in the SDK's stateless request scope, sees the project the API key resolved to on that request.
/// </summary>
[TestClass]
public sealed class McpServerEndpointTests : BaseTest<Module>
{
    private sealed record Seed(Guid AgentInProjectId, Guid AgentInOtherProjectId, string KeyValue);

    [TestMethod]
    public async Task ListAgents_OverHttp_ReturnsOnlyTheKeysProjectAgents()
    {
        await using var app = await StartHostAsync();
        var seed = await SeedAsync(app);

        await using var client = await ConnectAsync(app, seed.KeyValue);
        var result = await client.CallToolAsync(
            "list_agents", new Dictionary<string, object?>(), cancellationToken: CancellationToken);

        result.IsError.Should().NotBe(true);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        text.Should().Contain(seed.AgentInProjectId.ToString());
        text.Should().NotContain(seed.AgentInOtherProjectId.ToString());
    }

    [TestMethod]
    public async Task ListPrompts_OverHttp_ExposesWorkflowPromptsAndReturnsContent()
    {
        await using var app = await StartHostAsync();
        var seed = await SeedAsync(app);

        await using var client = await ConnectAsync(app, seed.KeyValue);

        var prompts = await client.ListPromptsAsync(cancellationToken: CancellationToken);
        var names = prompts.Select(p => p.Name).ToArray();
        names.Should().Contain("optimize_agent");
        names.Should().Contain("curate_suite");
        names.Should().Contain("review_proposals");

        var result = await client.GetPromptAsync("optimize_agent", null, cancellationToken: CancellationToken);
        result.Messages.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task Connect_WithUnknownApiKey_IsRejected()
    {
        await using var app = await StartHostAsync();

        await FluentActions
            .Invoking(() => ConnectAsync(app, "proxytrace-not-a-real-key"))
            .Should().ThrowAsync<Exception>();
    }

    private async Task<WebApplication> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
        builder.Host.ConfigureContainer<ContainerBuilder>(cb => cb.RegisterModule<Module>());

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("Mcp", policy => policy
                .AddAuthenticationSchemes(McpApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcp("/mcp").RequireAuthorization("Mcp");

        await app.StartAsync(CancellationToken);
        return app;
    }

    private async Task<Seed> SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var agentInProject = await sp.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var otherProject = await sp.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var agentFactory = sp.GetRequiredService<IAgent.CreateNew>();
        var agentRepo = sp.GetRequiredService<IAgentRepository>();
        var agentInOther = await agentRepo.AddAsync(
            agentFactory(agentInProject.Name + "-other", agentInProject.SystemPrompt, agentInProject.Tools,
                agentInProject.Endpoint, otherProject, agentInProject.ModelParameters),
            CancellationToken);

        var provider = await sp.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync(CancellationToken);
        var owner = await sp.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var createApiKey = sp.GetRequiredService<IApiKey.CreateNew>();
        var apiKeys = sp.GetRequiredService<IApiKeyRepository>();
        const string rawKey = "proxytrace-integration-key";
        await apiKeys.AddAsync(
            createApiKey("mcp-integration", Sha256.HexHash(rawKey), rawKey[..16], agentInProject.Project, provider, ApiKeyScopes.McpRead, owner),
            CancellationToken);

        return new Seed(agentInProject.Id, agentInOther.Id, rawKey);
    }

    private async Task<McpClient> ConnectAsync(WebApplication app, string apiKey)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" },
            },
            app.GetTestClient(),
            NullLoggerFactory.Instance,
            false);

        return await McpClient.CreateAsync(transport, null, NullLoggerFactory.Instance, CancellationToken);
    }
}
