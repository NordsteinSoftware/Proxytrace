using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Nordstein.Core.Common.DependencyInjection;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Guards the registration shape behind #451: one upstream LLM call was emitting four identical sets
/// of <c>System.Net.Http.HttpClient.openai.*</c> log lines — four handler instances logging one
/// request, on the hottest path in the system. <c>AddHttpClient</c> shares its plumbing through
/// <c>TryAddEnumerable</c>, which dedupes only within a single <see cref="IServiceCollection"/>; the
/// API host composes four modules (Api, Application, Licensing, Proxy) that each call it on their own
/// collection, so the container ended up with one logging filter per module.
/// </summary>
[TestClass]
public sealed class HttpClientRegistrationTests
{
    [TestMethod]
    public void RegisterServiceCollection_WithHttpClientsFromSeveralModules_RegistersOneLoggingFilter()
    {
        var builder = new ContainerBuilder();

        // Stands in for the four modules that each register their own named clients.
        builder.RegisterServiceCollection(services => services.AddHttpClient("openai"));
        builder.RegisterServiceCollection(services => services.AddHttpClient("passthrough"));
        builder.RegisterServiceCollection(services => services.AddHttpClient("license-server"));
        builder.RegisterServiceCollection(services => services.AddHttpClient("self"));

        using var container = builder.Build();

        var filters = container.Resolve<IEnumerable<IHttpMessageHandlerBuilderFilter>>().ToList();

        // One of each kind the framework ships (logging, metrics) — not one set per module. Every
        // duplicate logging filter wraps the request in another logging handler.
        filters.Should().OnlyHaveUniqueItems();
        filters.Select(f => f.GetType()).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void RegisterServiceCollection_WithHttpClientsFromSeveralModules_KeepsEveryNamedClient()
    {
        // Deduplicating the shared plumbing must not touch the per-name configuration, which is what
        // makes a named client actually usable — those are instance descriptors, not type ones.
        var builder = new ContainerBuilder();
        builder.RegisterServiceCollection(services =>
            services.AddHttpClient("openai", client => client.Timeout = TimeSpan.FromMinutes(5)));
        builder.RegisterServiceCollection(services =>
            services.AddHttpClient("passthrough", client => client.Timeout = TimeSpan.FromMinutes(3)));

        using var container = builder.Build();
        var factory = container.Resolve<IHttpClientFactory>();

        factory.CreateClient("openai").Timeout.Should().Be(TimeSpan.FromMinutes(5));
        factory.CreateClient("passthrough").Timeout.Should().Be(TimeSpan.FromMinutes(3));
    }
}
