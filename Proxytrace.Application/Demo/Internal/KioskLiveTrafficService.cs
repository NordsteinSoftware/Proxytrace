using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Common.Time;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.ModelEndpoint;

namespace Proxytrace.Application.Demo.Internal;

/// <summary>
/// Keeps the kiosk feeling alive after boot: continuously fabricates agent calls from the same
/// <see cref="DemoTrafficCatalog"/>/<see cref="DemoCallPlanner"/> material as the historical
/// backfill and publishes each one to the trace stream, so the dashboard's pulse band, live
/// telemetry, recent-traces feed and the Traces list keep moving without a real LLM endpoint.
/// The pace follows the catalog's diurnal curve (boosted — a kiosk audience should see a new
/// trace every few seconds at peak, not one a minute) with jittered gaps so arrivals read as
/// organic traffic rather than a metronome. Kiosk-only: the composition root swaps in a
/// <c>NullHostedService</c> outside kiosk mode.
/// </summary>
internal sealed class KioskLiveTrafficService : BackgroundService
{
    /// <summary>
    /// Live traffic runs this much faster than the backfill's historical rate. On stage the point
    /// is visible motion; a strict continuation of the historical rate would sit idle for half a
    /// minute at a time.
    /// </summary>
    private const double LiveRateBoost = 2.5;

    private const double MinDelaySeconds = 4;
    private const double MaxDelaySeconds = 120;

    private static readonly TimeSpan SeedPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SeedWaitTimeout = TimeSpan.FromMinutes(5);

    // The root provider is needed to open a fresh DI scope (and therefore a fresh storage
    // context) per emitted interaction — the same reason DemoSeederHostedService injects it.
    private readonly IServiceProvider rootServices;
    private readonly DemoSeedContext ctx;
    private readonly DemoCallPlanner planner;
    private readonly ITraceBroadcaster traceBroadcaster;
    private readonly IRandom random;
    private readonly IClock clock;
    private readonly ILogger<KioskLiveTrafficService> logger;

    public KioskLiveTrafficService(
        IServiceProvider rootServices,
        DemoSeedContext ctx,
        DemoCallPlanner planner,
        ITraceBroadcaster traceBroadcaster,
        IRandom random,
        IClock clock,
        ILogger<KioskLiveTrafficService> logger)
    {
        this.rootServices = rootServices;
        this.ctx = ctx;
        this.planner = planner;
        this.traceBroadcaster = traceBroadcaster;
        this.random = random;
        this.clock = clock;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await WaitForSeedAsync(stoppingToken))
        {
            return;
        }

        logger.LogInformation("Kiosk live traffic feed started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EmitInteractionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One failed fabrication must not kill the feed for the rest of the demo.
                logger.LogWarning(ex, "Kiosk live traffic emission failed; continuing");
            }

            await Task.Delay(NextDelay(), stoppingToken);
        }
    }

    /// <summary>
    /// The demo seeder (an <c>IHostedService</c> registered before this service) populates the
    /// context before this loop starts; the wait is a safety net for compositions that start
    /// hosted services concurrently, not an expected code path.
    /// </summary>
    private async Task<bool> WaitForSeedAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset deadline = clock.UtcNow + SeedWaitTimeout;
        while (ctx.CustomerSupportAgent is null)
        {
            if (clock.UtcNow > deadline)
            {
                logger.LogError(
                    "Kiosk live traffic feed disabled: demo seeding did not complete within {Timeout}",
                    SeedWaitTimeout);
                return false;
            }

            await Task.Delay(SeedPollInterval, stoppingToken);
        }

        return true;
    }

    /// <summary>
    /// Fabricates one interaction (a single call or a two-call tool round-trip) for a
    /// volume-weighted random agent, persists it, and publishes each call to the trace stream.
    /// Internal so tests can drive one emission directly without running the timed loop.
    /// </summary>
    internal async Task EmitInteractionAsync(CancellationToken cancellationToken)
    {
        var (traffic, agentId) = PickProfile();
        Guid endpointId = PickEndpointId(traffic);

        using var scope = rootServices.CreateScope();
        var services = scope.ServiceProvider;
        var agent = await services.GetRequiredService<IRepository<IAgent>>()
            .GetAsync(agentId, cancellationToken);
        var endpoint = await services.GetRequiredService<IRepository<IModelEndpoint>>()
            .GetAsync(endpointId, cancellationToken);
        var callFactory = services.GetRequiredService<IAgentCall.CreateNew>();
        var completionFactory = services.GetRequiredService<ICompletion.Create>();
        var paramsFactory = services.GetRequiredService<IModelParameters.Create>();
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();

        var plan = planner.Plan(traffic);
        Guid? conversationId = plan.SharesConversation ? Guid.NewGuid() : null;

        foreach (var planned in plan.Calls)
        {
            ICompletion? response = planned.ResponseMessage is null
                ? null
                : completionFactory(
                    planned.ResponseMessage,
                    planned.Usage,
                    TimeSpan.FromMilliseconds(planned.LatencyMs));

            var call = callFactory(
                agent: agent,
                version: agent.CurrentVersion,
                endpoint: endpoint,
                request: new Conversation([agent.CreateSystemMessage(), .. planned.RequestTail]),
                response: response,
                httpStatus: planned.HttpStatus,
                finishReason: planned.FinishReason,
                errorMessage: planned.ErrorMessage,
                modelParameters: paramsFactory(temperature: 0.3),
                conversationId: conversationId,
                outlierFlags: planned.OutlierFlags);

            call = await callRepo.AddAsync(call, cancellationToken);
            traceBroadcaster.Publish(TraceCreatedEvent.Create(call));
        }
    }

    private (DemoTrafficCatalog.AgentTraffic Traffic, Guid AgentId) PickProfile()
    {
        var profiles = Profiles();
        double total = profiles.Sum(p => AverageDailyVolume(p.Traffic));
        double pick = random.Double() * total;
        double acc = 0;
        foreach (var profile in profiles)
        {
            acc += AverageDailyVolume(profile.Traffic);
            if (pick <= acc)
            {
                return profile;
            }
        }
        return profiles[^1];
    }

    private Guid PickEndpointId(DemoTrafficCatalog.AgentTraffic traffic)
    {
        double pick = random.Double();
        double acc = 0;
        foreach (var share in traffic.EndpointMix)
        {
            acc += share.Weight;
            if (pick <= acc)
            {
                return EndpointId(share.Endpoint);
            }
        }
        return EndpointId(traffic.EndpointMix[^1].Endpoint);
    }

    private Guid EndpointId(DemoTrafficCatalog.DemoEndpointKey key)
        => key switch
        {
            DemoTrafficCatalog.DemoEndpointKey.Gpt54 => ctx.RequireGpt54Endpoint().Id,
            DemoTrafficCatalog.DemoEndpointKey.Gpt54Mini => ctx.RequireGpt54MiniEndpoint().Id,
            DemoTrafficCatalog.DemoEndpointKey.ClaudeSonnet => ctx.RequireClaudeEndpoint().Id,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown demo endpoint key"),
        };

    private (DemoTrafficCatalog.AgentTraffic Traffic, Guid AgentId)[] Profiles() =>
    [
        (DemoTrafficCatalog.Support, ctx.RequireCustomerSupportAgent().Id),
        (DemoTrafficCatalog.CodeReview, ctx.RequireCodeReviewAgent().Id),
        (DemoTrafficCatalog.Analytics, ctx.RequireDataAnalyticsAgent().Id),
        (DemoTrafficCatalog.Triage, ctx.RequireEmailTriageAgent().Id),
    ];

    private static double AverageDailyVolume(DemoTrafficCatalog.AgentTraffic traffic)
        => (traffic.MinCallsPerDay + traffic.MaxCallsPerDay) / 2.0;

    /// <summary>
    /// The mean gap between interactions follows the boosted diurnal rate for the current hour;
    /// each individual gap is jittered ±50% so the feed doesn't tick like a clock.
    /// </summary>
    private TimeSpan NextDelay()
    {
        int[] weights = DemoTrafficCatalog.DiurnalWeights;
        double hourShare = (double)weights[clock.UtcNow.Hour] / weights.Sum();
        double totalDaily = Profiles().Sum(p => AverageDailyVolume(p.Traffic));
        double interactionsPerHour = totalDaily * hourShare * LiveRateBoost;
        double meanSeconds = 3600.0 / Math.Max(interactionsPerHour, 1.0);
        double seconds = Math.Clamp(
            meanSeconds * random.Double(0.5, 1.5), MinDelaySeconds, MaxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
