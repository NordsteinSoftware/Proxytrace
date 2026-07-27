using System.Net;
using System.Text;
using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Common.Async;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Infrastructure.Internal;

namespace Proxytrace.Infrastructure.Tests;

[TestClass]
public sealed class LiteLlmCatalogResolverTests
{
    public required TestContext TestContext { get; init; }

    private const string Catalog =
        """
        {
          "sample_spec": { "input_cost_per_token": 0.0, "output_cost_per_token": 0.0 },
          "gpt-4o": { "input_cost_per_token": 0.0000025, "output_cost_per_token": 0.00001, "cache_read_input_token_cost": 0.00000125 },
          "azure/gpt-4o": { "input_cost_per_token": 0.000003, "output_cost_per_token": 0.00001 }
        }
        """;

    [TestMethod]
    public async Task Resolve_KnownModel_ConvertsUsdPerTokenToEurPer1M()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(0.9m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        var price = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(2.25m);
        price.OutputTokenCost.Should().Be(9.0m);
        // cache_read_input_token_cost 0.00000125 USD/token → 0.00000125 * 1M * 0.9 = 1.125 EUR/1M.
        price.CachedInputTokenCost.Should().Be(1.125m);
    }

    [TestMethod]
    public async Task Resolve_ModelWithoutCachedPrice_LeavesCachedNull()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        // azure/gpt-4o has input/output but no cache_read_input_token_cost.
        var price = await sut.ResolveAsync(["azure/gpt-4o"], TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(3.0m);
        price.CachedInputTokenCost.Should().BeNull();
    }

    [TestMethod]
    public async Task Resolve_TriesCandidatesInOrder_FirstMatchWins()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        // azure/gpt-4o (0.000003) precedes gpt-4o (0.0000025) → azure entry wins.
        var price = await sut.ResolveAsync(["azure/gpt-4o", "gpt-4o"], TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(3.0m);
    }

    [TestMethod]
    public async Task Resolve_FallsBackToLaterCandidate_WhenEarlierMissing()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        // azure/gpt-5 is absent → falls back to gpt-4o.
        var price = await sut.ResolveAsync(["azure/gpt-5", "gpt-4o"], TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(2.5m);
    }

    [TestMethod]
    public async Task Resolve_UnknownModel_ReturnsUnknown()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(0.9m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        var price = await sut.ResolveAsync(["does-not-exist"], TestContext.CancellationToken);

        price.Should().Be(ModelPrice.Unknown);
    }

    [TestMethod]
    public async Task Resolve_NoFxRate_ReturnsUnknown()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns((decimal?)null);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        var price = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        price.Should().Be(ModelPrice.Unknown);
    }

    [TestMethod]
    public async Task Resolve_AfterFailedFetch_RetriesInsteadOfCachingTheEmptyCatalog()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var handler = new SequencedHandler(
            _ => throw new HttpRequestException("transient"),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Catalog, Encoding.UTF8, "application/json") });
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        // First fetch fails: fail-soft to Unknown, but the empty result must not be cached...
        ModelPrice first = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
        // ...so the next call re-fetches and picks the catalog up.
        ModelPrice second = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        first.Should().Be(ModelPrice.Unknown);
        second.InputTokenCost.Should().Be(2.5m);
    }

    [TestMethod]
    public async Task Resolve_SuccessfulFetch_IsCachedAndNotRefetched()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var handler = new SequencedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Catalog, Encoding.UTF8, "application/json") },
            _ => throw new InvalidOperationException("catalog must not be fetched twice"));
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
        ModelPrice second = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        second.InputTokenCost.Should().Be(2.5m);
        handler.CallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task Resolve_WhenCancelled_PropagatesCancellationInsteadOfEmptyCatalog()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        using var cts = new CancellationTokenSource();
        var handler = new SequencedHandler(ct =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        });
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>());

        await FluentActions
            .Invoking(() => sut.ResolveAsync(["gpt-4o"], cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    /// <summary>
    /// Responds with a different scripted outcome per call, so a test can assert what happens
    /// across repeated fetches (retry after failure, or no second fetch at all).
    /// </summary>
    private sealed class SequencedHandler(params Func<CancellationToken, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int calls;

        public int CallCount => calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            int index = Interlocked.Increment(ref calls) - 1;
            Func<CancellationToken, HttpResponseMessage> response = responses[Math.Min(index, responses.Length - 1)];
            return Task.FromResult(response(ct));
        }
    }
}
