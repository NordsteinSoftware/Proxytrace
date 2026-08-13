using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nordstein.Core.Common.Hosting;

/// <summary>
/// Host-level wiring shared by every Proxytrace process host (the app API and the standalone
/// ingestion proxy).
/// </summary>
public static class HostingServiceCollectionExtensions
{
    /// <summary>
    /// Makes an unhandled exception in a <see cref="BackgroundService"/> non-fatal to the host.
    /// <para>
    /// .NET's default is <see cref="BackgroundServiceExceptionBehavior.StopHost"/>: one throwing
    /// background loop stops the whole host and the process exits with code <b>0</b> — a clean
    /// shutdown, indistinguishable from an intentional stop. Nothing restarts on a zero exit
    /// (`restart: on-failure`, most orchestrator defaults), the container simply stays down, and
    /// the log line explaining why is a single Critical entry the process is already too far gone
    /// to persist. That is how a licensed e2e `api` container went from healthy to gone in ~35s
    /// with no diagnosable trace ([#522](https://github.com/NordsteinSoftware/Proxytrace/issues/522)).
    /// </para>
    /// <para>
    /// The trade is deliberate: with <see cref="BackgroundServiceExceptionBehavior.Ignore"/> the
    /// faulted service stops but every other one — and the HTTP surface — keeps serving, and the
    /// framework logs the fault at <c>Error</c> level. That level is exactly what
    /// <c>ErrorLogChannelLoggerProvider</c> captures, so the crash lands in the in-product error
    /// log instead of dying with the process. Losing one background loop is a degraded feature;
    /// losing the host is a total outage.
    /// </para>
    /// <para>
    /// This only covers <see cref="BackgroundService.ExecuteAsync"/> faults. The startup-critical
    /// work — schema initialization/migrations (<c>DatabaseInitializationService</c>), the secret
    /// and preview backfills, the seeders — is implemented as plain <see cref="IHostedService"/>
    /// with the work in <c>StartAsync</c>, which still aborts startup when it throws. Keep it that
    /// way: an API that came up against an unmigrated database must not serve traffic.
    /// </para>
    /// </summary>
    public static IServiceCollection AddResilientBackgroundServices(this IServiceCollection services) =>
        services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
}
