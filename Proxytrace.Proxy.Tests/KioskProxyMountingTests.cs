using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Messaging;
using Proxytrace.Proxy.Controllers;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Guards the kiosk-showcase mounting rule that <c>Proxytrace.Api/Program.cs</c> implements: the
/// OpenAI-compatible proxy controller (which lives in this shared library and is referenced by the API)
/// is mounted as an MVC application part — so its <c>openai/v1/{**path}</c> routes resolve — ONLY when
/// kiosk mode runs with a live <c>Kiosk:Endpoint</c>. In production and kiosk-without-endpoint the route
/// must not exist (404). The mounted route with no proxy API key returns 401, which distinguishes "route
/// matched" from "route absent".
///
/// The real API does NOT get the part by choice: the Web SDK auto-generates
/// <c>[assembly: ApplicationPart("Proxytrace.Proxy")]</c> into <c>Proxytrace.Api</c> at build time, so the
/// part is present in every mode and Program.cs must actively STRIP it outside kiosk+endpoint. This test
/// reproduces that by seeding the part unconditionally before applying the same mount/strip decision.
/// </summary>
[TestClass]
public sealed class KioskProxyMountingTests
{
    [TestMethod]
    [DataRow(true, true, false, DisplayName = "kiosk + live endpoint -> route mounted (401, not 404)")]
    [DataRow(true, false, true, DisplayName = "kiosk without endpoint -> route absent (404)")]
    [DataRow(false, false, true, DisplayName = "non-kiosk (production) -> route absent (404)")]
    public async Task ProxyRoute_IsMounted_OnlyForKioskWithLiveEndpoint(
        bool kioskEnabled, bool endpointConfigured, bool expectRouteAbsent)
    {
        await using var app = await StartHostAsync(kioskEnabled, endpointConfigured);
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            // No Authorization header on purpose: a mounted route rejects with 401, an absent one 404s.
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
                "the mounted route matched and rejected the keyless request");
        }
    }

    private static async Task<WebApplication> StartHostAsync(bool kioskEnabled, bool endpointConfigured)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

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

        // Reproduce the Web SDK's build-time behaviour: it auto-generates
        // [assembly: ApplicationPart("Proxytrace.Proxy")] into Proxytrace.Api, so the proxy controller's
        // assembly is present as an application part in EVERY mode. Seed it unconditionally here so the
        // strip path below is actually exercised.
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(apm =>
                apm.ApplicationParts.Add(new AssemblyPart(typeof(OpenAiProxyController).Assembly)));

        var app = builder.Build();

        // Mirror Program.cs exactly: keep the proxy part only when kiosk runs with a live endpoint,
        // otherwise strip the auto-added part. Done post-build (before MapControllers) so the action
        // descriptors reflect the decision.
        var mount = app.Services.GetRequiredService<KioskOptions>().Enabled
                    && app.Services.GetRequiredService<KioskEndpointOptions>().IsConfigured;
        ApplyMountDecision(app.Services.GetRequiredService<ApplicationPartManager>(), mount);

        app.MapControllers();

        await app.StartAsync(CancellationToken.None);
        return app;
    }

    // Mirror of Proxytrace.Api.Kiosk.KioskProxyMounting.Apply (this test project does not reference the
    // API host). Keep the proxy assembly's part when mounting; strip the SDK's auto-added part otherwise.
    private static void ApplyMountDecision(ApplicationPartManager partManager, bool mount)
    {
        var proxyAssembly = typeof(OpenAiProxyController).Assembly;
        var existingPart = partManager.ApplicationParts
            .OfType<AssemblyPart>()
            .FirstOrDefault(part => part.Assembly == proxyAssembly);

        if (mount)
        {
            if (existingPart is null)
            {
                partManager.ApplicationParts.Add(new AssemblyPart(proxyAssembly));
            }
        }
        else if (existingPart is not null)
        {
            partManager.ApplicationParts.Remove(existingPart);
        }
    }
}
