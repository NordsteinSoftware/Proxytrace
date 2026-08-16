using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal;

internal sealed class TraceIndexPrunerService : BackgroundService
{
    private readonly LuceneIndexWriter writer;
    private readonly SearchConfiguration configuration;
    private readonly ILogger<TraceIndexPrunerService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceIndexPrunerService"/> class.
    /// </summary>
    public TraceIndexPrunerService(
        LuceneIndexWriter writer,
        SearchConfiguration configuration,
        ILogger<TraceIndexPrunerService> logger)
    {
        this.writer = writer;
        this.configuration = configuration;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromHours(Math.Max(1, configuration.PrunerIntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                PruneOnce();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trace index prune failed");
            }
            try
            {
                await Task.Delay(period, stoppingToken);
            }
            catch (TaskCanceledException) { return; }
        }
    }

    private void PruneOnce()
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-configuration.TraceRetentionDays).UtcTicks;
        var query = new BooleanQuery
        {
            { new TermQuery(new Term(SearchConstants.FieldKind, SearchKind.AgentCall.ToString())), Occur.MUST },
            { NumericRangeQuery.NewInt64Range(SearchConstants.FieldCreatedAt, null, cutoff, minInclusive: true, maxInclusive: false), Occur.MUST },
        };
        writer.DeleteByQuery(query);
    }
}
