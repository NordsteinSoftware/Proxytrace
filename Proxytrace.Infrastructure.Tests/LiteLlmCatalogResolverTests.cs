using System.Net;
using System.Text;
using AwesomeAssertions;
using NSubstitute;
using Nordstein.Core.Common.Async;
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

    /// <summary>
    /// Comfortably longer than the resolver's (private) FailedFetchRetryInterval — advancing the test
    /// clock by this expires the negative cache a failed fetch armed.
    /// </summary>
    private static readonly TimeSpan PastNegativeCache = TimeSpan.FromMinutes(5);

    [TestMethod]
    public async Task Resolve_KnownModel_ConvertsUsdPerTokenToEurPer1M()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(0.9m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

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
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

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
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

        // azure/gpt-4o (0.000003) precedes gpt-4o (0.0000025) → azure entry wins.
        var price = await sut.ResolveAsync(["azure/gpt-4o", "gpt-4o"], TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(3.0m);
    }

    [TestMethod]
    public async Task Resolve_FallsBackToLaterCandidate_WhenEarlierMissing()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

        // azure/gpt-5 is absent → falls back to gpt-4o.
        var price = await sut.ResolveAsync(["azure/gpt-5", "gpt-4o"], TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(2.5m);
    }

    [TestMethod]
    public async Task Resolve_UnknownModel_ReturnsUnknown()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(0.9m);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

        var price = await sut.ResolveAsync(["does-not-exist"], TestContext.CancellationToken);

        price.Should().Be(ModelPrice.Unknown);
    }

    [TestMethod]
    public async Task Resolve_NoFxRate_ReturnsUnknown()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns((decimal?)null);
        var sut = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(HttpStatusCode.OK, Catalog)), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

        var price = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        price.Should().Be(ModelPrice.Unknown);
    }

    [TestMethod]
    public async Task Resolve_AfterFailedFetch_RetriesInsteadOfCachingTheEmptyCatalog()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var clock = new MutableClock();
        var handler = new SequencedHandler(
            _ => throw new HttpRequestException("transient"),
            _ => Ok());
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), clock);

        // First fetch fails: fail-soft to Unknown, but the empty result must not be cached...
        ModelPrice first = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
        // ...so once the short negative cache expires the next call re-fetches and picks the catalog up.
        clock.Advance(PastNegativeCache);
        ModelPrice second = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        first.Should().Be(ModelPrice.Unknown);
        second.InputTokenCost.Should().Be(2.5m);
    }

    [TestMethod]
    public async Task Resolve_DuringCatalogOutage_AttemptsOnlyOneFetchForRepeatedCalls()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var handler = new SequencedHandler(_ => throw new HttpRequestException("catalog is down"));
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

        // A provider exposing many models resolves a price per model; the outage must not turn that
        // into one outbound fetch attempt per model (#478).
        for (int i = 0; i < 10; i++)
        {
            ModelPrice price = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
            price.Should().Be(ModelPrice.Unknown);
        }

        handler.CallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task Resolve_AfterNegativeCacheExpires_RetriesTheFetch()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var clock = new MutableClock();
        var handler = new SequencedHandler(_ => throw new HttpRequestException("catalog is down"));
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), clock);

        await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
        await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
        handler.CallCount.Should().Be(1, "the second call is still inside the negative-cache window");

        clock.Advance(PastNegativeCache);
        await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        handler.CallCount.Should().Be(2);
    }

    [TestMethod]
    public async Task Resolve_WhenCancelled_DoesNotArmTheNegativeCache()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        using var cts = new CancellationTokenSource();
        var handler = new SequencedHandler(
            ct =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("unreachable");
            },
            _ => Ok());
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

        await FluentActions
            .Invoking(() => sut.ResolveAsync(["gpt-4o"], cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        // Caller-initiated cancellation is not a catalog failure, so the very next call must fetch
        // again rather than sit out the negative-cache window.
        ModelPrice price = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(2.5m);
        handler.CallCount.Should().Be(2);
    }

    [TestMethod]
    public async Task Resolve_SuccessfulFetchAfterFailure_CachesTheCatalog()
    {
        var fx = Substitute.For<IFxRateProvider>();
        fx.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(1.0m);
        var clock = new MutableClock();
        var handler = new SequencedHandler(
            _ => throw new HttpRequestException("transient"),
            _ => Ok(),
            _ => throw new InvalidOperationException("catalog must not be fetched after it was cached"));
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), clock);

        await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
        clock.Advance(PastNegativeCache);
        await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);
        // The recovered catalog is cached for good — a later call neither re-fetches nor is affected
        // by the earlier failure.
        clock.Advance(PastNegativeCache);
        ModelPrice third = await sut.ResolveAsync(["gpt-4o"], TestContext.CancellationToken);

        third.InputTokenCost.Should().Be(2.5m);
        handler.CallCount.Should().Be(2);
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
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

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
        var sut = new LiteLlmCatalogResolver(new HttpClient(handler), new PricingOptions(), fx, Substitute.For<IAsyncLock>(), new MutableClock());

        await FluentActions
            .Invoking(() => sut.ResolveAsync(["gpt-4o"], cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent(Catalog, Encoding.UTF8, "application/json") };

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
