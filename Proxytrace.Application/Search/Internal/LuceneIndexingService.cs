using System.Threading.Channels;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Search.Internal.Mappers;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal;

internal sealed class LuceneIndexingService : BackgroundService, ISearchIndexer
{
    private const int MaxBatchSize = 500;
    private static readonly TimeSpan LingerWindow = TimeSpan.FromMilliseconds(25);

    private readonly LuceneIndexWriter writer;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IReindexStateTracker reindexTracker;
    private readonly ILogger<LuceneIndexingService> logger;

    private readonly Channel<IndexRequest> channel = Channel.CreateUnbounded<IndexRequest>(
        new UnboundedChannelOptions { SingleReader = true });

    // Deliberate exception to the "always IAsyncLock" rule (see docs/code-style.md §Concurrency):
    // this guards a synchronous idle-signal swap with no await in the critical section.
    private readonly Lock idleLock = new();
    private TaskCompletionSource idleSignal = CreateIdleSignal();
    private int pendingCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuceneIndexingService"/> class.
    /// </summary>
    public LuceneIndexingService(
        LuceneIndexWriter writer,
        IServiceScopeFactory scopeFactory,
        IReindexStateTracker reindexTracker,
        ILogger<LuceneIndexingService> logger)
    {
        this.writer = writer;
        this.scopeFactory = scopeFactory;
        this.reindexTracker = reindexTracker;
        this.logger = logger;

        // queue starts empty → already idle
        idleSignal.TrySetResult();
    }

    private int started;

    /// <summary>
    /// Starts asynchronously.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return Task.CompletedTask;
        }
        return base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Indexes asynchronously.
    /// </summary>
    public Task IndexAsync(SearchKind kind, Guid projectId, Guid entityId, CancellationToken cancellationToken = default)
        => EnqueueAsync(new IndexRequest(kind, entityId, Remove: false), cancellationToken);

    /// <summary>
    /// Removes asynchronously.
    /// </summary>
    public Task RemoveAsync(SearchKind kind, Guid entityId, CancellationToken cancellationToken = default)
        => EnqueueAsync(new IndexRequest(kind, entityId, Remove: true), cancellationToken);

    /// <summary>
    /// Reindexes the project asynchronously.
    /// </summary>
    public async Task ReindexProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        using var _ = reindexTracker.BeginReindex(projectId);

        using var scope = scopeFactory.CreateScope();
        var settingsResolver = scope.ServiceProvider.GetRequiredService<IProjectSearchSettingsResolver>();
        var mappers = ResolveMappers(scope);

        var settings = await settingsResolver.GetOrDefaultsAsync(projectId, cancellationToken);
        var allowed = settings.IndexedKinds.ToHashSet();

        var projectFilter = new TermQuery(new Term(SearchConstants.FieldProjectId, projectId.ToString()));
        writer.DeleteByQuery(projectFilter);

        foreach (var (kind, mapper) in mappers)
        {
            if (!allowed.Contains(kind))
            {
                continue;
            }

            var docs = mapper.BuildAllForProjectAsync(projectId, cancellationToken);
            await foreach (Document doc in docs)
            {
                var id = doc.Get(SearchConstants.FieldId);
                writer.UpsertDeferred(id, doc);
            }
        }

        writer.CommitAndRefresh();
    }

    private static IReadOnlyDictionary<SearchKind, IDocumentMapper> ResolveMappers(IServiceScope scope)
        => scope.ServiceProvider.GetServices<IDocumentMapper>()
            .GroupBy(m => m.Kind)
            .ToDictionary(g => g.Key, g => g.First());

    /// <summary>
    /// Flushes asynchronously.
    /// </summary>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task signal;
        lock (idleLock)
        {
            signal = idleSignal.Task;
        }
        return signal.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new Dictionary<(SearchKind Kind, Guid Id), IndexRequest>(capacity: MaxBatchSize);

        try
        {
            while (await channel.Reader.WaitToReadAsync(stoppingToken))
            {
                batch.Clear();
                DrainAvailable(batch);
                await LingerAsync(batch, stoppingToken);
                await FlushBatchAsync(batch, stoppingToken);
                MarkProcessed(batch.Count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LuceneIndexingService crashed");
        }
    }

    private Task EnqueueAsync(IndexRequest request, CancellationToken cancellationToken)
    {
        BeginPending();
        return channel.Writer.WriteAsync(request, cancellationToken).AsTask();
    }

    private void DrainAvailable(Dictionary<(SearchKind, Guid), IndexRequest> batch)
    {
        while (batch.Count < MaxBatchSize && channel.Reader.TryRead(out var req))
        {
            batch[(req.Kind, req.EntityId)] = req;
        }
    }

    private async Task LingerAsync(Dictionary<(SearchKind, Guid), IndexRequest> batch, CancellationToken stoppingToken)
    {
        if (batch.Count >= MaxBatchSize)
        {
            return;
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(LingerWindow);
        try
        {
            while (batch.Count < MaxBatchSize && await channel.Reader.WaitToReadAsync(cts.Token))
            {
                DrainAvailable(batch);
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // linger expired; flush what we have
        }
    }

    private async Task FlushBatchAsync(Dictionary<(SearchKind, Guid), IndexRequest> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var mappers = ResolveMappers(scope);
        var settingsResolver = scope.ServiceProvider.GetRequiredService<IProjectSearchSettingsResolver>();

        foreach (var req in batch.Values)
        {
            try
            {
                var key = $"{req.Kind}:{req.EntityId}";
                if (req.Remove)
                {
                    writer.DeleteDeferred(key);
                    continue;
                }
                if (!mappers.TryGetValue(req.Kind, out var mapper))
                {
                    continue;
                }
                var doc = await mapper.BuildAsync(req.EntityId, cancellationToken);
                if (doc is null)
                {
                    writer.DeleteDeferred(key);
                    continue;
                }
                // Respect per-project search settings centrally (the gate used to live in the
                // repository decorator). A project with search disabled, auto-reindex off, or
                // this kind excluded gets no incremental update — manual reindex still forces it.
                if (!await IsIndexingAllowedAsync(settingsResolver, doc, req.Kind, cancellationToken))
                {
                    continue;
                }
                writer.UpsertDeferred(key, doc);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to index {Kind} {EntityId}", req.Kind, req.EntityId);
            }
        }

        try
        {
            writer.CommitAndRefresh();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to commit index batch ({Count} items)", batch.Count);
        }
    }

    private static async Task<bool> IsIndexingAllowedAsync(
        IProjectSearchSettingsResolver resolver,
        Document doc,
        SearchKind kind,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(doc.Get(SearchConstants.FieldProjectId), out var projectId))
        {
            return false;
        }
        var settings = await resolver.GetOrDefaultsAsync(projectId, cancellationToken);
        return settings.Enabled && settings.AutoReindexOnChange && settings.IndexedKinds.Contains(kind);
    }

    private void BeginPending()
    {
        lock (idleLock)
        {
            if (pendingCount == 0 && idleSignal.Task.IsCompleted)
            {
                idleSignal = CreateIdleSignal();
            }
            pendingCount++;
        }
    }

    private void MarkProcessed(int count)
    {
        if (count == 0)
        {
            return;
        }
        lock (idleLock)
        {
            pendingCount = Math.Max(0, pendingCount - count);
            if (pendingCount == 0)
            {
                idleSignal.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CreateIdleSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly record struct IndexRequest(SearchKind Kind, Guid EntityId, bool Remove);
}
