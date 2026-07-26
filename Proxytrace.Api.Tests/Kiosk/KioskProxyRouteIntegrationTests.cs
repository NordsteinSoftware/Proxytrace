using System.Net;
using System.Reflection;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Kiosk;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Messaging;
using Proxytrace.Proxy;
using Proxytrace.Proxy.Controllers;

namespace Proxytrace.Api.Tests.Kiosk;

/// <summary>
/// Integration guard for Finding C1: the ASP.NET Core Web SDK auto-generates
/// <c>[assembly: ApplicationPart("Proxytrace.Proxy")]</c> into <c>Proxytrace.Api</c>, so the
/// OpenAI-compatible proxy controller is present in EVERY build. Unless the composition root strips it,
/// the <c>openai/v1/{**path}</c> route would resolve in production and kiosk-without-endpoint — 500ing on
/// unresolvable dependencies instead of returning the promised 404.
///
/// Rather than boot the whole <c>Program.cs</c> (which needs Postgres/hosted services and mutates process
/// env), this test reproduces the exact failure surface: it seeds an MVC host's
/// <see cref="ApplicationPartManager"/> from the REAL <c>Proxytrace.Api</c> assembly's
/// <see cref="ApplicationPartAttribute"/>s — the very attribute the SDK generated — then applies the
/// production <see cref="KioskProxyMounting.Apply"/> decision and asserts the route's status through a
/// real MVC pipeline for all three gate combinations. It fails if C1 regresses (the strip is removed) or
/// if the SDK ever stops emitting the part (the precondition assertion).
/// </summary>
[TestClass]
public sealed class KioskProxyRouteIntegrationTests
{
    [TestMethod]
    public void Precondition_ProxyControllerAssembly_IsAutoAddedAsApplicationPartOfTheApi()
    {
        // If this ever fails the SDK stopped auto-generating the part (e.g. the proxy library was
        // renamed or made non-MVC) and the strip logic — and these tests — would silently no-op.
        var apiParts = typeof(Proxytrace.Api.Module).Assembly
            .GetCustomAttributes<ApplicationPartAttribute>()
            .Select(attribute => attribute.AssemblyName);

        apiParts.Should().Contain(
            typeof(OpenAiProxyController).Assembly.GetName().Name,
            "the Web SDK auto-adds the controller-bearing Proxytrace.Proxy library as an MVC application part");
    }

    [TestMethod]
    [DataRow(false, false, true, DisplayName = "production (non-kiosk) -> route absent (404)")]
    [DataRow(true, false, true, DisplayName = "kiosk without endpoint -> route absent (404)")]
    [DataRow(true, true, false, DisplayName = "kiosk + live endpoint -> route mounted (401, not 404/500)")]
    public async Task ProxyRoute_Status_MatchesGate_WithSdkAutoAddedPart(
        bool kioskEnabled, bool endpointConfigured, bool expectRouteAbsent)
    {
        await using var app = await StartHostAsync(kioskEnabled, endpointConfigured);
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            // No Authorization header on purpose: a mounted route rejects keyless with 401; an absent
            // one 404s. A 500 here would mean the controller mounted without its dependencies (the C1 bug).
            Content = new StringContent("""{"model":"gpt-4o","messages":[]}""", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        if (expectRouteAbsent)
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "the proxy route must not exist outside kiosk mode with a live endpoint");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "the mounted route matched and rejected the keyless request (not 404 absent, not 500 unresolvable)");
        }
    }

    private static async Task<WebApplication> StartHostAsync(bool kioskEnabled, bool endpointConfigured)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Proxy controller dependencies — present so the mounted route can reach its 401 rejection
        // instead of 500ing (which is precisely what would prove the C1 regression).
        builder.Services.AddSingleton(Substitute.For<IIngestionStream>());
        builder.Services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory("{}"));

        var resolver = Substitute.For<IApiKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((ResolvedApiKey?)null);
        builder.Services.AddSingleton(resolver);
        builder.Services.AddSingleton(Substitute.For<IRequestBlocker>());
        builder.Services.AddSingleton(Substitute.For<IBudgetBlocker>());

        builder.Services.AddSingleton(new KioskOptions { Enabled = kioskEnabled });
        builder.Services.AddSingleton(endpointConfigured
            ? new KioskEndpointOptions { BaseUrl = "https://api.example-llm.com/v1", ApiKey = "sk-live", Model = "demo-gpt" }
            : new KioskEndpointOptions());

        // Reproduce the SDK's build-time auto-discovery: seed the application-part manager from the REAL
        // Proxytrace.Api assembly's ApplicationPart attributes, exactly as the framework would if Api were
        // the entry assembly. This is what puts the proxy controller in play in every mode.
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(SeedFromApiAssemblyParts);

        var app = builder.Build();

        // Apply the production mount/strip decision (the same gate Program.cs uses).
        var mount = app.Services.GetRequiredService<KioskOptions>().Enabled
                    && app.Services.GetRequiredService<KioskEndpointOptions>().IsConfigured;
        KioskProxyMounting.Apply(app.Services.GetRequiredService<ApplicationPartManager>(), mount);

        app.MapControllers();

        await app.StartAsync(CancellationToken.None);
        return app;
    }

    private static void SeedFromApiAssemblyParts(ApplicationPartManager partManager)
    {
        var apiAssembly = typeof(Proxytrace.Api.Module).Assembly;
        foreach (var attribute in apiAssembly.GetCustomAttributes<ApplicationPartAttribute>())
        {
            var partAssembly = Assembly.Load(new AssemblyName(attribute.AssemblyName));
            var factory = ApplicationPartFactory.GetApplicationPartFactory(partAssembly);
            foreach (var part in factory.GetApplicationParts(partAssembly))
            {
                partManager.ApplicationParts.Add(part);
            }
        }
    }
}
