using Autofac;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Text;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.PerfHarness.Bootstrap;
using Proxytrace.PerfHarness.Reporting;

namespace Proxytrace.PerfHarness.Scenarios;

/// <summary>
/// Times the proxy's per-request credential resolution against real Postgres (#407). Since the
/// proxy deliberately resolves credentials from storage on <b>every</b> request (no positive cache —
/// a cached snapshot would delay key rotation/revocation), this path is paid per proxied call and
/// must stay flat as the database grows. The scenario mirrors <c>ApiKeyResolver.ResolveAsync</c>'s
/// repository call sequence — keep it in sync with that class — and runs each iteration in a fresh
/// lifetime scope, because the per-request scope/DbContext construction is part of the cost the
/// proxy actually pays.
/// </summary>
internal static class ApiKeyResolutionScenario
{
    private const string RawProxytraceKey = "proxytrace-perf-resolution-key";
    private const string RawUpstreamKey = "perf-upstream-resolution-key";

    public static async Task<IReadOnlyList<MetricResult>> RunAsync(
        PerfContainer container,
        PerfBudgets budgets,
        int warmup,
        int iterations,
        CancellationToken cancellationToken)
    {
        string projectSlug = await EnsureCredentialsSeededAsync(container, cancellationToken);

        var results = new List<MetricResult>();

        async Task Measure(string name, Func<ILifetimeScope, Task> request)
        {
            var (p50, p95) = await PerfReport.MeasureLatencyAsync(
                warmup,
                iterations,
                () => container.InScopeAsync(scope => request(scope)));
            Console.WriteLine($"[db-layer] {name,-26} p50={p50,8:N1}ms  p95={p95,8:N1}ms");
            results.Add(new MetricResult("db-layer", name, p95, budgets.DbQueryBudget(name), "ms", BudgetDirection.LowerIsBetter));
        }

        // Proxytrace-issued key path: one blind-index lookup + provider/project/owner hydration
        // (including the upstream-key decrypt in the mapper).
        await Measure("proxyResolveProxytraceKey", async scope =>
        {
            var resolved = await scope.Resolve<IApiKeyRepository>().FindByKeyAsync(RawProxytraceKey, cancellationToken);
            if (resolved is null)
            {
                throw new InvalidOperationException("Seeded Proxytrace key did not resolve — seeding bug.");
            }
        });

        // Upstream-provider-key path, in the resolver's real order: the Proxytrace-key lookup
        // misses first, then the provider blind-index lookup and the project slug resolution.
        await Measure("proxyResolveUpstreamKey", async scope =>
        {
            await scope.Resolve<IApiKeyRepository>().FindByKeyAsync(RawUpstreamKey, cancellationToken);
            var provider = await scope.Resolve<IModelProviderRepository>().FindByApiKeyAsync(RawUpstreamKey, cancellationToken);
            var project = await scope.Resolve<IProjectRepository>().FindBySlugAsync(projectSlug, cancellationToken);
            if (provider is null || project is null)
            {
                throw new InvalidOperationException("Seeded upstream credentials did not resolve — seeding bug.");
            }
        });

        return results;
    }

    /// <summary>
    /// Idempotently seeds one Proxytrace-issued key and one dedicated provider with a known
    /// upstream key, attached to the seeded perf graph. Guarded by the blind-index lookups, so
    /// repeat runs against a kept database reuse the existing rows.
    /// </summary>
    private static Task<string> EnsureCredentialsSeededAsync(PerfContainer container, CancellationToken cancellationToken)
        => container.InScopeAsync(async scope =>
        {
            var project = await scope.Resolve<IRepository<IProject>>().FindFirstAsync(cancellationToken)
                          ?? throw new InvalidOperationException("No project found — run `seed` against this database first.");

            var apiKeys = scope.Resolve<IApiKeyRepository>();
            if (await apiKeys.FindByKeyAsync(RawProxytraceKey, cancellationToken) is null)
            {
                var provider = await scope.Resolve<IRepository<IModelProvider>>().FindFirstAsync(cancellationToken)
                               ?? throw new InvalidOperationException("No provider found — run `seed` against this database first.");
                var owner = await scope.Resolve<IDomainEntityGenerator<IUser>>().GetOrCreateAsync(cancellationToken);
                var key = scope.Resolve<IApiKey.CreateNew>()(
                    name: "perf resolution key",
                    keyHash: Sha256.HexHash(RawProxytraceKey),
                    keyPrefix: RawProxytraceKey[..16],
                    project: project,
                    provider: provider,
                    scopes: ApiKeyScopes.Ingestion,
                    owner: owner);
                await scope.Resolve<IRepository<IApiKey>>().AddAsync(key, cancellationToken);
            }

            var providers = scope.Resolve<IModelProviderRepository>();
            if (await providers.FindByApiKeyAsync(RawUpstreamKey, cancellationToken) is null)
            {
                var upstreamProvider = scope.Resolve<IModelProvider.CreateNew>()(
                    name: "Perf Resolution Provider",
                    endpoint: new Uri("https://api.perf-resolution.example.com/v1"),
                    apiKey: RawUpstreamKey,
                    kind: ModelProviderKind.OpenAiCompatible);
                await providers.AddAsync(upstreamProvider, cancellationToken);
            }

            return project.Name.ToSlug();
        });
}
