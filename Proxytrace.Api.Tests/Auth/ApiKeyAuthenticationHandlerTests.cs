using System.Text.Encodings.Web;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Mcp;
using Proxytrace.Api.Auth.Rest;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests.Auth;

[TestClass]
public sealed class ApiKeyAuthenticationHandlerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Authenticate_WithApiReadKey_SucceedsAndAttributesToOwner()
    {
        IServiceProvider services = GetServices();
        var apiKey = await SeedKeyAsync(services, "proxytrace-rest-read", ApiKeyScopes.ApiRead);

        var (handler, context) = await BuildHandlerAsync(services, $"Bearer proxytrace-rest-read");
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        // Acts as its owner and records the key id for audit attribution.
        context.Items[CurrentUserAccessor.UserIdItemKey].Should().Be(apiKey.Owner.Id);
        context.Items[McpApiKeyAuthenticationHandler.ApiKeyIdItemKey].Should().Be(apiKey.Id);
        var principal = result.Principal ?? throw new InvalidOperationException("Expected a principal.");
        principal.HasClaim(ApiKeyAuthenticationHandler.ScopeClaimType, nameof(ApiKeyScopes.ApiRead))
            .Should().BeTrue();
        principal.HasClaim(ApiKeyAuthenticationHandler.ScopeClaimType, nameof(ApiKeyScopes.ApiWrite))
            .Should().BeFalse();
    }

    [TestMethod]
    public async Task Authenticate_WithApiWriteKey_EmitsWriteScopeClaim()
    {
        IServiceProvider services = GetServices();
        await SeedKeyAsync(services, "proxytrace-rest-write", ApiKeyScopes.ApiRead | ApiKeyScopes.ApiWrite);

        var (handler, _) = await BuildHandlerAsync(services, "Bearer proxytrace-rest-write");
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        var principal = result.Principal ?? throw new InvalidOperationException("Expected a principal.");
        principal.HasClaim(ApiKeyAuthenticationHandler.ScopeClaimType, nameof(ApiKeyScopes.ApiWrite))
            .Should().BeTrue();
    }

    [TestMethod]
    public async Task Authenticate_WithMcpOnlyKey_Fails()
    {
        IServiceProvider services = GetServices();
        await SeedKeyAsync(services, "proxytrace-mcp-only", ApiKeyScopes.McpRead | ApiKeyScopes.McpWrite);

        var (handler, context) = await BuildHandlerAsync(services, "Bearer proxytrace-mcp-only");
        var result = await handler.AuthenticateAsync();

        // Keys are not interchangeable across surfaces: an MCP key cannot drive REST.
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        context.Items.Should().NotContainKey(CurrentUserAccessor.UserIdItemKey);
    }

    [TestMethod]
    public async Task Authenticate_WithUnknownKey_Fails()
    {
        IServiceProvider services = GetServices();

        var (handler, _) = await BuildHandlerAsync(services, "Bearer proxytrace-does-not-exist");
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Authenticate_WithNonProxytraceBearer_ReturnsNoResult()
    {
        IServiceProvider services = GetServices();

        // A JWT bearer is left to the JwtBearer handler — no DB lookup, no failure.
        var (handler, _) = await BuildHandlerAsync(services, "Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig");
        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [TestMethod]
    public async Task Authenticate_WithoutBearerHeader_ReturnsNoResult()
    {
        IServiceProvider services = GetServices();

        var (handler, _) = await BuildHandlerAsync(services, authorization: null);
        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    private static async Task<IApiKey> SeedKeyAsync(IServiceProvider services, string rawKey, ApiKeyScopes scopes)
    {
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync();
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>().CreateAsync();
        var owner = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync();
        var createApiKey = services.GetRequiredService<IApiKey.CreateNew>();
        var apiKeys = services.GetRequiredService<IApiKeyRepository>();
        return await apiKeys.AddAsync(
            createApiKey("rest", Sha256.HexHash(rawKey), rawKey[..16], project, provider, scopes, owner),
            CancellationToken.None);
    }

    private static async Task<(ApiKeyAuthenticationHandler Handler, DefaultHttpContext Context)> BuildHandlerAsync(
        IServiceProvider services, string? authorization)
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(Arg.Any<string?>()).Returns(new AuthenticationSchemeOptions());

        var handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            services.GetRequiredService<IApiKeyRepository>());

        var context = new DefaultHttpContext();
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;

        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return (handler, context);
    }
}
