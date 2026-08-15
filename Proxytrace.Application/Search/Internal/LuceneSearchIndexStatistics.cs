using Lucene.Net.Index;
using Lucene.Net.Search;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal;

internal sealed class LuceneSearchIndexStatistics : ISearchIndexStatistics
{
    private readonly LuceneIndexWriter writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuceneSearchIndexStatistics"/> class.
    /// </summary>
    public LuceneSearchIndexStatistics(LuceneIndexWriter writer)
    {
        this.writer = writer;
    }

    /// <summary>
    /// Counts asynchronously.
    /// </summary>
    public Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var query = new TermQuery(new Term(SearchConstants.FieldProjectId, projectId.ToString()));
        using var reader = writer.AcquireReader();
        var top = reader.Searcher.Search(query, 1);
        return Task.FromResult(top.TotalHits);
    }

    /// <summary>
    /// Last indexed at asynchronously.
    /// </summary>
    public Task<DateTimeOffset?> LastIndexedAtAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var query = new TermQuery(new Term(SearchConstants.FieldProjectId, projectId.ToString()));
        var sort = new Sort(new SortField(SearchConstants.FieldCreatedAt, SortFieldType.INT64, reverse: true));

        using var reader = writer.AcquireReader();
        var top = reader.Searcher.Search(query, 1, sort);
        if (top.ScoreDocs.Length == 0)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }

        var doc = reader.Searcher.Doc(top.ScoreDocs[0].Doc);
        var field = doc.GetField(SearchConstants.FieldCreatedAt);
        if (field is null)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }

        long? ticks = field.GetInt64Value();
        return ticks is null ? Task.FromResult<DateTimeOffset?>(null) : Task.FromResult<DateTimeOffset?>(new DateTimeOffset(ticks.Value, TimeSpan.Zero));
    }
}
