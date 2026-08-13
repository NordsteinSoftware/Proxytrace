using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Domain.Events;

namespace Proxytrace.Application.Statistics.Internal.Worker;

internal class StatisticsHostedService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly IEntityEventService entityEvents;
    private readonly IReadOnlyDictionary<Type, IReadOnlyList<IStatsProjector>> projectorsByType;
    private readonly ILogger<StatisticsHostedService> logger;

    public StatisticsHostedService(
        IEntityEventService entityEvents,
        IEnumerable<IStatsProjector> projectors,
        ILogger<StatisticsHostedService> logger)
    {
        this.entityEvents = entityEvents;
        this.logger = logger;
        projectorsByType = projectors
            .GroupBy(p => p.EntityType)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<IStatsProjector>)g.ToArray());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (projectorsByType.Count == 0)
        {
            logger.LogInformation("No stats projectors registered; statistics hosted service idle.");
            return;
        }

        ChannelReader<EntityChangedEvent> reader = entityEvents.Subscribe(stoppingToken);
        Dictionary<(Type, Guid), EntityChangedEvent> pending = new();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(stoppingToken))
                {
                    return;
                }

                // Debounce: wait briefly to coalesce a burst, then drain everything queued.
                await Task.Delay(FlushInterval, stoppingToken);

                while (reader.TryRead(out EntityChangedEvent? evt))
                {
                    if (projectorsByType.ContainsKey(evt.EntityType))
                    {
                        pending[(evt.EntityType, evt.EntityId)] = evt;
                    }
                }

                if (pending.Count == 0)
                {
                    continue;
                }

                foreach (EntityChangedEvent evt in pending.Values)
                {
                    foreach (IStatsProjector projector in projectorsByType[evt.EntityType])
                    {
                        try
                        {
                            await projector.ProjectAsync(evt.EntityId, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex,
                                "Stats projector {Projector} failed for {EntityType} {EntityId}",
                                projector.GetType().Name, evt.EntityType.Name, evt.EntityId);
                        }
                    }
                }

                pending.Clear();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Statistics hosted service drain loop encountered an error");
            }
        }
    }
}
