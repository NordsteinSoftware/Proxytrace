using Autofac;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proxytrace.Common.DependencyInjection;
using Proxytrace.Common.Time;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Licensing;
using Proxytrace.Proxy.Internal;

namespace Proxytrace.Proxy;

/// <summary>
/// Registers the shared OpenAI-compatible proxy pipeline: the API-key resolver, blocking-rule
/// provider, request blocker, and the in-process services they depend on (IMemoryCache, HTTP
/// clients). The host composition root is responsible for registering the storage, messaging,
/// infrastructure, and licensing modules that the pipeline's dependencies resolve against.
/// </summary>
public sealed class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        // Deliberately uncached (#407): a shared positive credential cache would keep forwarding a
        // rotated upstream key — and keep accepting a revoked inbound one — until its TTL expired,
        // in every proxy replica independently. Resolution hits storage on every request and fails
        // closed when the database is unreachable. Per-request lifetime, NOT SingleInstance: the
        // resolver captures its repositories (and their per-call StorageDbContext), which must not
        // pin against the root scope.
        builder.RegisterType<ApiKeyResolver>()
            .As<IApiKeyResolver>()
            .InstancePerLifetimeScope();

        // Real-time blocking anomaly detectors: same per-lifetime-scope reasoning as the key
        // resolver above (the repository holds a per-call StorageDbContext); the shared state is
        // the singleton IMemoryCache. TTL is read from the container-registered IConfiguration so
        // both the standalone host and any future in-process mount use their own config.
        builder.Register(ctx =>
        {
            var config = ctx.Resolve<IConfiguration>();
            var ttlSeconds = config.GetSection("BlockingRuleCache").GetValue<int?>("TtlSeconds") ?? 30;
            return new CachedBlockingRuleProvider(
                ctx.Resolve<ICustomAnomalyDetectorRepository>(),
                ctx.Resolve<ILicenseService>(),
                ctx.Resolve<IMemoryCache>(),
                TimeSpan.FromSeconds(ttlSeconds),
                ctx.Resolve<ILogger<CachedBlockingRuleProvider>>());
        })
        .As<IBlockingRuleProvider>()
        .InstancePerLifetimeScope();

        builder.RegisterType<RequestBlocker>()
            .As<IRequestBlocker>()
            .InstancePerLifetimeScope();

        // Monthly cost budgets: same shape and lifetime reasoning as the blocking-rule provider
        // above — a per-call storage repository behind a singleton IMemoryCache, TTL from config.
        builder.Register(ctx =>
        {
            var config = ctx.Resolve<IConfiguration>();
            var ttlSeconds = config.GetSection("BudgetBlockCache").GetValue<int?>("TtlSeconds") ?? 30;
            return new CachedBudgetBlockProvider(
                ctx.Resolve<ICostLimitBreachRepository>(),
                ctx.Resolve<ILicenseService>(),
                ctx.Resolve<IMemoryCache>(),
                ctx.Resolve<IClock>(),
                TimeSpan.FromSeconds(ttlSeconds),
                ctx.Resolve<ILogger<CachedBudgetBlockProvider>>());
        })
        .As<IBudgetBlockProvider>()
        .InstancePerLifetimeScope();

        builder.RegisterType<BudgetBlocker>()
            .As<IBudgetBlocker>()
            .InstancePerLifetimeScope();

        builder.RegisterServiceCollection(services =>
        {
            services.AddMemoryCache();
            // The upstream target is per-request (provider endpoint), so only the timeout matters
            // here. That timeout is also the budget OpenAiProxyController applies to the *body*
            // download: it reads with ResponseHeadersRead (to avoid buffering the whole reply), and
            // HttpClient.Timeout stops applying once the headers land, so the controller re-arms this
            // same value on its copy loop (#475). Changing it here changes both phases.
            // Auto-redirect is off for the same reason as the pass-through client below: a
            // transparent proxy relays a 3xx to the client rather than chasing it server-side. The
            // BCL's redirect handler only clears the typed Authorization header on a cross-origin
            // hop — the Azure `api-key` header (BuildUpstreamRequest) and every blanket-forwarded
            // client header survive the hop, so following an upstream redirect would hand the
            // provider credential to whatever host the upstream named.
            services.AddHttpClient("openai", client => client.Timeout = TimeSpan.FromMinutes(5))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

            // Non-LLM pass-through relays redirects to the client verbatim instead of following them
            // server-side (the BCL would also strip Authorization on the redirect hop), so it needs
            // its own handler with auto-redirect off.
            services.AddHttpClient("passthrough", client => client.Timeout = TimeSpan.FromMinutes(5))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        });
    }
}
