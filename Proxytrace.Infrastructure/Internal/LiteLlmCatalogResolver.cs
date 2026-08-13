using System.Collections.ObjectModel;
using System.Text.Json;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Time;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Infrastructure.Internal;

/// <summary>
/// Resolves prices from the LiteLLM catalog (USD per token), converting to EUR / 1M tokens via the
/// FX provider. The catalog is fetched once and cached in memory; a failed fetch is not cached but
/// suppresses further attempts for <see cref="FailedFetchRetryInterval"/>. Used for all provider
/// kinds — Azure providers pass an <c>azure/&lt;model&gt;</c> candidate ahead of the bare model name.
/// </summary>
internal sealed class LiteLlmCatalogResolver
{
    // How long a failed (or empty) catalog fetch suppresses further fetch attempts. Sized to be
    // longer than a single model refresh — which resolves a price per discovered model, so during an
    // outage it would otherwise queue one fetch attempt per model — while still short enough that an
    // operator who retries after a blip sees real prices rather than a stale outage state (#478).
    private static readonly TimeSpan FailedFetchRetryInterval = TimeSpan.FromSeconds(30);

    // Instance-scoped lock key (the resolver is a singleton). A per-instance Guid is not a constant,
    // so it must not be a static field; it only needs to be stable for the lifetime of this resolver
    // to serialize the one-shot catalog fetch.
    private readonly Guid lockKey = Guid.NewGuid();

    private readonly HttpClient http;
    private readonly PricingOptions options;
    private readonly IFxRateProvider fxRateProvider;
    private readonly IAsyncLock gate;
    private readonly IClock clock;
    // volatile: the double-checked fast path below reads this outside the lock; the write happens
    // under the gate. volatile guarantees the freshly-fetched catalog is visible to those lock-free
    // reads without tearing.
    private volatile IReadOnlyDictionary<string, (decimal? Input, decimal? Output, decimal? CachedInput)>? cache;

    // UTC ticks before which no fetch is attempted (0 = no failure recorded). Read on the lock-free
    // fast path and written under the gate; a 64-bit field cannot be volatile, so it is accessed with
    // Interlocked to keep the read atomic on 32-bit runtimes.
    private long retryNotBeforeTicks;

    public LiteLlmCatalogResolver(
        HttpClient http,
        PricingOptions options,
        IFxRateProvider fxRateProvider,
        IAsyncLock gate,
        IClock clock)
    {
        this.http = http;
        this.options = options;
        this.fxRateProvider = fxRateProvider;
        this.gate = gate;
        this.clock = clock;
    }

    /// <summary>
    /// Resolves the price for the first <paramref name="candidateModelNames"/> present in the
    /// catalog (tried in order). Returns <see cref="ModelPrice.Unknown"/> if none match or the FX
    /// rate is unavailable.
    /// </summary>
    public async Task<ModelPrice> ResolveAsync(
        IReadOnlyList<string> candidateModelNames,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, (decimal? Input, decimal? Output, decimal? CachedInput)> catalog =
            await GetCatalogAsync(cancellationToken);

        (decimal? Input, decimal? Output, decimal? CachedInput)? entry = null;
        foreach (string name in candidateModelNames)
        {
            if (catalog.TryGetValue(name, out var found))
            {
                entry = found;
                break;
            }
        }

        if (entry is null)
            return ModelPrice.Unknown;

        decimal? fx = await fxRateProvider.GetUsdToEurAsync(cancellationToken);
        return fx is null
            ? ModelPrice.Unknown
            : new ModelPrice(
                ToEurPer1M(entry.Value.Input, fx.Value),
                ToEurPer1M(entry.Value.Output, fx.Value),
                ToEurPer1M(entry.Value.CachedInput, fx.Value));
    }

    private static decimal? ToEurPer1M(decimal? usdPerToken, decimal fx)
        => usdPerToken * 1_000_000m * fx;

    private async Task<IReadOnlyDictionary<string, (decimal?, decimal?, decimal?)>> GetCatalogAsync(
        CancellationToken cancellationToken)
    {
        if (cache is not null)
            return cache;

        // Outage fast path: bail out before queueing on the gate, so a refresh that resolves a price
        // per discovered model does not serialize one fetch attempt per model behind the gate.
        if (IsRetrySuppressed())
            return ReadOnlyDictionary<string, (decimal?, decimal?, decimal?)>.Empty;

        using var _ = await gate.LockAsync(lockKey, cancellationToken);
        if (cache is not null)
            return cache;
        if (IsRetrySuppressed())
            return ReadOnlyDictionary<string, (decimal?, decimal?, decimal?)>.Empty;

        IReadOnlyDictionary<string, (decimal?, decimal?, decimal?)> fetched = await FetchAsync(cancellationToken);

        // Only a fetch that actually produced entries is cached. A failed or empty fetch is never
        // cached as the catalog — that would pin every model price to ModelPrice.Unknown for the rest
        // of the process lifetime after a single network blip. It instead arms a short *negative*
        // cache: for the next FailedFetchRetryInterval callers get ModelPrice.Unknown without a
        // fetch, then exactly one caller retries. That keeps the recover-after-a-blip property while
        // collapsing a refresh of a provider's N models into a single outbound attempt per interval
        // (#478). A caller-cancelled fetch never reaches here — FetchAsync rethrows — so cancellation
        // does not arm the negative cache.
        if (fetched.Count == 0)
        {
            Interlocked.Exchange(ref retryNotBeforeTicks, clock.UtcNow.Add(FailedFetchRetryInterval).UtcTicks);
            return fetched;
        }

        // Once the catalog is cached both checks above short-circuit, so a stale suppression
        // timestamp can never be read again.
        cache = fetched;
        return fetched;
    }

    private bool IsRetrySuppressed()
    {
        long notBefore = Interlocked.Read(ref retryNotBeforeTicks);
        return notBefore > 0 && clock.UtcNow.UtcTicks < notBefore;
    }

    private async Task<IReadOnlyDictionary<string, (decimal?, decimal?, decimal?)>> FetchAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, (decimal?, decimal?, decimal?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using HttpResponseMessage response = await http.GetAsync(options.LiteLlmFeedUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return result;

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;
                result[prop.Name] = (
                    ReadDecimal(prop.Value, "input_cost_per_token"),
                    ReadDecimal(prop.Value, "output_cost_per_token"),
                    ReadDecimal(prop.Value, "cache_read_input_token_cost"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-initiated cancellation is not a catalog failure — never swallow it into an
            // empty (and previously permanently cached) result.
            throw;
        }
        catch
        {
            // fail-soft: empty catalog → callers get ModelPrice.Unknown. GetCatalogAsync never caches
            // it as the catalog; it arms a short negative cache and retries once that expires.
            return new Dictionary<string, (decimal?, decimal?, decimal?)>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    private static decimal? ReadDecimal(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDecimal()
            : null;
}