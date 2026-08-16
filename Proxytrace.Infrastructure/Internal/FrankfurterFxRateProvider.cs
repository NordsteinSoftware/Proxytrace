using System.Text.Json;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Time;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Infrastructure.Internal;

/// <summary>
/// USD→EUR via the free, no-key Frankfurter (ECB) API. A successful rate is cached for the calendar
/// day; a failed fetch is not cached as a rate but suppresses further attempts for
/// <see cref="FailedFetchRetryInterval"/>.
/// </summary>
internal sealed class FrankfurterFxRateProvider : IFxRateProvider
{
    private const string CacheGateKey = "fx-rate:usd-eur";

    // How long a failed FX fetch suppresses further fetch attempts. Sized to be longer than a single
    // model refresh — which resolves a price per discovered model, and every resolved price needs the
    // FX rate, so during an outage it would otherwise queue one fetch attempt per model — while still
    // short enough that an operator who retries after a blip sees real prices rather than a stale
    // outage state (#487, mirroring #478 one layer up).
    private static readonly TimeSpan FailedFetchRetryInterval = TimeSpan.FromSeconds(30);

    private readonly HttpClient http;
    private readonly PricingOptions options;
    private readonly IAsyncLock asyncLock;
    private readonly IClock clock;

    // volatile: the double-checked fast path below reads this outside the lock; the write happens
    // under the gate. Rate and day live in one immutable record so a lock-free reader can never see a
    // fresh rate paired with a stale day — a decimal and a DateOnly are two non-atomic writes, a
    // single reference is one.
    private volatile CachedRate? cached;

    // UTC ticks before which no fetch is attempted (0 = no failure recorded). Read on the lock-free
    // fast path and written under the gate; a 64-bit field cannot be volatile, so it is accessed with
    // Interlocked to keep the read atomic on 32-bit runtimes.
    private long retryNotBeforeTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrankfurterFxRateProvider"/> class.
    /// </summary>
    public FrankfurterFxRateProvider(HttpClient http, PricingOptions options, IAsyncLock asyncLock, IClock clock)
    {
        this.http = http;
        this.options = options;
        this.asyncLock = asyncLock;
        this.clock = clock;
    }

    /// <summary>
    /// Gets the usd to eur asynchronously.
    /// </summary>
    public async Task<decimal?> GetUsdToEurAsync(CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        CachedRate? hit = cached;
        if (hit is not null && hit.Day == today)
            return hit.Rate;

        // Outage fast path: bail out before queueing on the gate, so a refresh that resolves a price
        // per discovered model does not serialize one fetch attempt per model behind the gate.
        if (IsRetrySuppressed())
            return null;

        using IDisposable sync = await asyncLock.LockAsync(CacheGateKey, cancellationToken);

        hit = cached;
        if (hit is not null && hit.Day == today)
            return hit.Rate;
        if (IsRetrySuppressed())
            return null;

        decimal? rate = await FetchAsync(cancellationToken);

        // Only a fetch that actually produced a rate is cached for the day. A failed fetch instead
        // arms a short *negative* cache: for the next FailedFetchRetryInterval callers get null
        // without a fetch, then exactly one caller retries. That keeps the recover-after-a-blip
        // property while collapsing a refresh of a provider's N models into a single outbound attempt
        // per interval (#487). A caller-cancelled fetch never reaches here — FetchAsync rethrows — so
        // cancellation does not arm the negative cache.
        if (rate is null)
        {
            Interlocked.Exchange(ref retryNotBeforeTicks, clock.UtcNow.Add(FailedFetchRetryInterval).UtcTicks);
            return null;
        }

        cached = new CachedRate(rate.Value, today);
        return rate;
    }

    private bool IsRetrySuppressed()
    {
        long notBefore = Interlocked.Read(ref retryNotBeforeTicks);
        return notBefore > 0 && clock.UtcNow.UtcTicks < notBefore;
    }

    private async Task<decimal?> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{options.FxApiUrl}?from=USD&to=EUR";
            using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("rates", out JsonElement rates)
                && rates.TryGetProperty("EUR", out JsonElement eur)
                && eur.ValueKind == JsonValueKind.Number)
            {
                return eur.GetDecimal();
            }
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-initiated cancellation is not an FX failure — never swallow it into a null rate
            // that would arm the negative cache and sit out the retry window for everyone else.
            throw;
        }
        catch
        {
            // fail-soft: no rate → callers get ModelPrice.Unknown. GetUsdToEurAsync never caches it as
            // the day's rate; it arms a short negative cache and retries once that expires.
            return null;
        }
    }

    /// <summary>
    /// The rate cached for a given calendar day, held as one immutable reference so the lock-free
    /// fast path always observes a consistent (rate, day) pair.
    /// </summary>
    private sealed record CachedRate(decimal Rate, DateOnly Day);
}
