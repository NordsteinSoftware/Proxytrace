using System.Net;
using System.Text;
using AwesomeAssertions;
using Nordstein.Core.Common.Async;
using Proxytrace.Infrastructure.Internal;

namespace Proxytrace.Infrastructure.Tests;

[TestClass]
public sealed class FrankfurterFxRateProviderTests
{
    public required TestContext TestContext { get; init; }

    private const string RateBody =
        """{"amount":1.0,"base":"USD","date":"2026-06-09","rates":{"EUR":0.92}}""";

    /// <summary>
    /// Comfortably longer than the provider's (private) FailedFetchRetryInterval — advancing the test
    /// clock by this expires the negative cache a failed fetch armed, while staying inside the same
    /// calendar day so the positive cache is unaffected.
    /// </summary>
    private static readonly TimeSpan PastNegativeCache = TimeSpan.FromMinutes(5);

    [TestMethod]
    public async Task GetUsdToEur_ParsesRate()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RateBody);
        var sut = new FrankfurterFxRateProvider(new HttpClient(handler), new PricingOptions(), new NoOpAsyncLock(), new MutableClock());

        var rate = await sut.GetUsdToEurAsync(TestContext.CancellationToken);

        rate.Should().Be(0.92m);
    }

    [TestMethod]
    public async Task GetUsdToEur_OnFailure_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        var sut = new FrankfurterFxRateProvider(new HttpClient(handler), new PricingOptions(), new NoOpAsyncLock(), new MutableClock());

        (await sut.GetUsdToEurAsync(TestContext.CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task GetUsdToEur_DuringFxOutage_AttemptsOnlyOneFetchForRepeatedCalls()
    {
        var handler = new SequencedHandler(_ => throw new HttpRequestException("fx feed is down"));
        var sut = new FrankfurterFxRateProvider(new HttpClient(handler), new PricingOptions(), new NoOpAsyncLock(), new MutableClock());

        // A provider exposing many models resolves a price per model and every price needs the FX
        // rate; the outage must not turn that into one outbound fetch attempt per model (#487).
        for (int i = 0; i < 10; i++)
        {
            decimal? rate = await sut.GetUsdToEurAsync(TestContext.CancellationToken);
            rate.Should().BeNull();
        }

        handler.CallCount.Should().Be(1);
    }

    [TestMethod]
    public async Task GetUsdToEur_AfterNegativeCacheExpires_RetriesTheFetch()
    {
        var clock = new MutableClock();
        var handler = new SequencedHandler(_ => throw new HttpRequestException("fx feed is down"));
        var sut = new FrankfurterFxRateProvider(new HttpClient(handler), new PricingOptions(), new NoOpAsyncLock(), clock);

        await sut.GetUsdToEurAsync(TestContext.CancellationToken);
        await sut.GetUsdToEurAsync(TestContext.CancellationToken);
        handler.CallCount.Should().Be(1, "the second call is still inside the negative-cache window");

        clock.Advance(PastNegativeCache);
        await sut.GetUsdToEurAsync(TestContext.CancellationToken);

        handler.CallCount.Should().Be(2);
    }

    [TestMethod]
    public async Task GetUsdToEur_WhenCancelled_DoesNotArmTheNegativeCache()
    {
        using var cts = new CancellationTokenSource();
        var handler = new SequencedHandler(
            ct =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("unreachable");
            },
            _ => Ok());
        var sut = new FrankfurterFxRateProvider(new HttpClient(handler), new PricingOptions(), new NoOpAsyncLock(), new MutableClock());

        await FluentActions
            .Invoking(() => sut.GetUsdToEurAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        // Caller-initiated cancellation is not an FX failure, so the very next call must fetch again
        // rather than sit out the negative-cache window.
        decimal? rate = await sut.GetUsdToEurAsync(TestContext.CancellationToken);

        rate.Should().Be(0.92m);
        handler.CallCount.Should().Be(2);
    }

    [TestMethod]
    public async Task GetUsdToEur_SuccessfulFetchAfterFailure_CachesTheRate()
    {
        var clock = new MutableClock();
        var handler = new SequencedHandler(
            _ => throw new HttpRequestException("transient"),
            _ => Ok(),
            _ => throw new InvalidOperationException("the rate must not be fetched after it was cached"));
        var sut = new FrankfurterFxRateProvider(new HttpClient(handler), new PricingOptions(), new NoOpAsyncLock(), clock);

        (await sut.GetUsdToEurAsync(TestContext.CancellationToken)).Should().BeNull();
        clock.Advance(PastNegativeCache);
        (await sut.GetUsdToEurAsync(TestContext.CancellationToken)).Should().Be(0.92m);

        // The recovered rate is cached for the rest of the calendar day — a later call neither
        // re-fetches nor is affected by the earlier failure.
        clock.Advance(PastNegativeCache);
        decimal? third = await sut.GetUsdToEurAsync(TestContext.CancellationToken);

        third.Should().Be(0.92m);
        handler.CallCount.Should().Be(2);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent(RateBody, Encoding.UTF8, "application/json") };

    private sealed class NoOpAsyncLock : IAsyncLock
    {
        public IDisposable Lock(object key) => new Handle();
        public Task<IDisposable> LockAsync(object key, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDisposable>(new Handle());

        private sealed class Handle : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    /// <summary>
    /// Responds with a different scripted outcome per call, so a test can assert what happens across
    /// repeated fetches (retry after failure, or no second fetch at all).
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
