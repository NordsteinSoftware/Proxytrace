using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ModelEndpointCostTests : DomainTest<Module>
{
    private async Task<IModelEndpoint> MakeEndpoint(
        IServiceProvider services, decimal? inputCost, decimal? outputCost, decimal? cachedCost)
    {
        var factory = services.GetRequiredService<IModelEndpoint.CreateNew>();
        var model = await GetOrCreate<IModel>(services);
        var provider = await GetOrCreate<IModelProvider>(services);
        return factory(model, provider, inputCost, outputCost, cachedCost);
    }

    [TestMethod]
    public async Task CalculateCost_WithCachedPrice_PricesCachedSubsetCheaper()
    {
        IServiceProvider services = GetServices();
        // Prices are EUR per 1M tokens.
        var endpoint = await MakeEndpoint(services, inputCost: 2m, outputCost: 10m, cachedCost: 1m);

        // 800 of the 1000 input tokens are cached.
        var cost = endpoint.CalculateCost(new TokenUsage(1000, 50, 800));

        // ((1000-800)*2 + 800*1 + 50*10) / 1_000_000 = (400 + 800 + 500) / 1e6
        cost.Should().Be(1700m / 1_000_000m);
    }

    [TestMethod]
    public async Task CalculateCost_NoCachedPrice_PricesCachedAtInputRate()
    {
        IServiceProvider services = GetServices();
        var endpoint = await MakeEndpoint(services, inputCost: 2m, outputCost: 10m, cachedCost: null);

        var withCached = endpoint.CalculateCost(new TokenUsage(1000, 50, 800));
        var withoutCached = endpoint.CalculateCost(new TokenUsage(1000, 50, 0));

        // No cached price → cached tokens fall back to the input rate, so the cached count is moot.
        withCached.Should().Be(withoutCached);
        withCached.Should().Be(2500m / 1_000_000m);
    }

    [TestMethod]
    public async Task CalculateCost_CachedExceedsInput_IsClampedToInput()
    {
        IServiceProvider services = GetServices();
        var endpoint = await MakeEndpoint(services, inputCost: 2m, outputCost: 10m, cachedCost: 1m);

        // Absurd cached count (500 > 100 input) must clamp to the input count, never underflow.
        var cost = endpoint.CalculateCost(new TokenUsage(100, 0, 500));

        // clamp cached → 100; (0*2 + 100*1 + 0*10) / 1e6
        cost.Should().Be(100m / 1_000_000m);
    }

    [TestMethod]
    public async Task CalculateCost_MissingInputPrice_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var endpoint = await MakeEndpoint(services, inputCost: null, outputCost: 10m, cachedCost: 1m);

        endpoint.CalculateCost(new TokenUsage(1000, 50, 800)).Should().BeNull();
    }

    [TestMethod]
    public async Task Activation_InputCostExceedsOutputCost_IsAccepted()
    {
        IServiceProvider services = GetServices();

        // Some batch/cached/reasoning tiers price input >= output; this must be valid (the factory
        // delegate runs activation validation, which previously rejected input > output).
        var endpoint = await MakeEndpoint(services, inputCost: 10m, outputCost: 2m, cachedCost: 1m);

        endpoint.Should().NotBeNull();
        // (1000*10 + 50*2) / 1_000_000 — cost still computes regardless of the input/output ordering.
        endpoint.CalculateCost(new TokenUsage(1000, 50, 0)).Should().Be(10_100m / 1_000_000m);
    }
}
