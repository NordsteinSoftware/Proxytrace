using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;
using Proxytrace.Proxy.Controllers;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Routing/precedence guard over real ASP.NET endpoint routing (in-memory TestServer, no DB/Redis).
/// The traced <c>{project}/openai/v1/{**path}</c> route and the proxy's own <c>/health</c> must keep
/// winning over the new all-parameter <c>{project}/{**rest}</c> pass-through catch-all. Precedence is
/// proven by behavior: the traced action ingests, the pass-through action does not.
/// </summary>
[TestClass]
public sealed class OpenAiProxyRoutingTests
{
    [TestMethod]
    public async Task RootHealth_IsNotShadowedByProjectCatchAll()
    {
        await using var app = await StartHostAsync(Substitute.For<IIngestionStream>());
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/health", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(CancellationToken.None))
            .Should().Contain("ok", "the proxy's own /health minimal endpoint must out-rank the {project}/{**rest} catch-all");
    }

    [TestMethod]
    public async Task TracedOpenAiRoute_StillMatches_AndIngests()
    {
        var stream = Substitute.For<IIngestionStream>();
        await using var app = await StartHostAsync(stream);
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/acme/openai/v1/chat/completions");
        request.Headers.Add("Authorization", "Bearer valid");
        request.Content = new StringContent("""{"model":"gpt-4o","messages":[]}""", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, CancellationToken.None);
        await response.Content.ReadAsStringAsync(CancellationToken.None); // drain so the action completes

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await stream.Received(1).PublishAsync(Arg.Any<IngestMessage>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task PassthroughRoute_Matches_AndDoesNotIngest()
    {
        var stream = Substitute.For<IIngestionStream>();
        var capture = new CapturingHttpMessageHandler("""{"status":"ok"}""");
        await using var app = await StartHostAsync(stream, new SingleHandlerClientFactory(capture));
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/acme/health");
        request.Headers.Add("Authorization", "Bearer valid");

        var response = await client.SendAsync(request, CancellationToken.None);
        await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capture.LastUri.Should().Be(new Uri("http://upstream.test/health"),
            "the pass-through route matched and forwarded to the upstream origin root");
        await stream.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [TestMethod]
    [DataRow("/openai/v1")]
    [DataRow("/acme/openai/v1")]
    public async Task TracedRoute_EmptyCatchAll_DoesNotThrow(string url)
    {
        // {**path} matches zero segments and binds null (#305) — must not 500 with an NRE.
        var stream = Substitute.For<IIngestionStream>();
        await using var app = await StartHostAsync(stream, new SingleHandlerClientFactory(
            new CapturingHttpMessageHandler("""{"status":"ok"}""")));
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", "Bearer valid");

        var response = await client.SendAsync(request, CancellationToken.None);
        await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an empty catch-all path must bind as empty, not null-ref into a 500");
    }

    [TestMethod]
    public async Task LegacyOpenAiRoute_StillMatches_AndIngests()
    {
        var stream = Substitute.For<IIngestionStream>();
        var capture = new CapturingHttpMessageHandler("""{"object":"list","data":[]}""");
        await using var app = await StartHostAsync(stream, new SingleHandlerClientFactory(capture));
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/openai/v1/models");
        request.Headers.Add("Authorization", "Bearer valid");

        var response = await client.SendAsync(request, CancellationToken.None);
        await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        capture.LastUri.Should().Be(new Uri("http://upstream.test/v1/models"),
            "the legacy no-project traced route forwards to the versioned endpoint, not the origin root");
        await stream.Received(1).PublishAsync(Arg.Any<IngestMessage>(), Arg.Any<CancellationToken>());
    }

    // ── host ────────────────────────────────────────────────────────────────────

    private static async Task<WebApplication> StartHostAsync(
        IIngestionStream stream, IHttpClientFactory? httpClientFactory = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(stream);
        builder.Services.AddSingleton(httpClientFactory ?? new FakeHttpClientFactory("""{"status":"ok"}"""));
        builder.Services.AddSingleton<IApiKeyResolver>(ResolverForAnyKey());
        builder.Services.AddSingleton(Substitute.For<IRequestBlocker>());
        builder.Services.AddSingleton(Substitute.For<IBudgetBlocker>());
        builder.Services.AddSingleton(new KioskOptions());
        builder.Services.AddSingleton(new KioskEndpointOptions());
        builder.Services.AddControllers().AddApplicationPart(typeof(OpenAiProxyController).Assembly);

        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        await app.StartAsync(CancellationToken.None);
        return app;
    }

    private static IApiKeyResolver ResolverForAnyKey()
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Id.Returns(Guid.NewGuid());
        provider.Name.Returns("test-provider");
        provider.ApiKey.Returns("sk-upstream");
        provider.Endpoint.Returns(new Uri("http://upstream.test/v1"));

        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());

        var resolver = Substitute.For<IApiKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvedApiKey(project, provider));
        return resolver;
    }
}
