using System.Net;
using System.Text.Json;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal;

internal sealed class LuceneSearchService : ISearchService
{
    private readonly LuceneIndexWriter writer;
    private readonly SearchConfiguration configuration;
    private readonly IProjectSearchSettingsResolver settingsResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuceneSearchService"/> class.
    /// </summary>
    public LuceneSearchService(
        LuceneIndexWriter writer,
        SearchConfiguration configuration,
        IProjectSearchSettingsResolver settingsResolver)
    {
        this.writer = writer;
        this.configuration = configuration;
        this.settingsResolver = settingsResolver;
    }

    /// <summary>
    /// Searches asynchronously.
    /// </summary>
    public async Task<SearchResults> SearchAsync(Guid projectId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResults([]);
        }

        var settings = await settingsResolver.GetOrDefaultsAsync(projectId, cancellationToken);
        if (!settings.Enabled || settings.IndexedKinds.Count == 0)
        {
            return new SearchResults([]);
        }

        using var analyzer = new StandardAnalyzer(LuceneIndexWriter.Version);
        var parser = new MultiFieldQueryParser(
            LuceneIndexWriter.Version,
            [SearchConstants.FieldTitle, SearchConstants.FieldBody, SearchConstants.FieldBoostedBody],
            analyzer)
        {
            DefaultOperator = QueryParserBase.AND_OPERATOR,
            // Leading wildcards are required for substring matching of content tokens
            // (PrefixQueryRewriter wraps bare terms as *term*).
            AllowLeadingWildcard = true,
        };

        Query parsed;
        try
        {
            parsed = parser.Parse(PrefixQueryRewriter.Rewrite(query));
        }
        catch (ParseException)
        {
            try
            {
                parsed = parser.Parse(QuerySanitizer.Escape(query));
            }
            catch (ParseException)
            {
                return new SearchResults([]);
            }
        }

        var combined = new BooleanQuery
        {
            { parsed, Occur.MUST },
            { new TermQuery(new Term(SearchConstants.FieldProjectId, projectId.ToString())), Occur.MUST },
        };

        var kindFilter = new BooleanQuery();
        foreach (var kind in settings.IndexedKinds)
        {
            kindFilter.Add(new TermQuery(new Term(SearchConstants.FieldKind, kind.ToString())), Occur.SHOULD);
        }
        combined.Add(kindFilter, Occur.MUST);

        using var reader = writer.AcquireReader();
        var searcher = reader.Searcher;

        int top = Math.Max(20, configuration.HitsPerKind * settings.IndexedKinds.Count * 4);
        var topDocs = searcher.Search(combined, top);

        int snippetMax = settings.SnippetLength;
        var formatter = new SimpleHTMLFormatter("<mark>", "</mark>");
        var scorer = new QueryScorer(parsed);
        var highlighter = new Highlighter(formatter, scorer)
        {
            TextFragmenter = new SimpleSpanFragmenter(scorer, snippetMax),
            // HTML-encode the body text while inserting <mark> tags so user/LLM content
            // (agent prompts, traces, test cases) can't inject markup into the snippet.
            // The default encoder is a no-op, which is the stored-XSS source.
            Encoder = new SimpleHTMLEncoder(),
        };

        var grouped = new Dictionary<SearchKind, List<SearchHit>>();
        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var kindStr = doc.Get(SearchConstants.FieldKind);
            if (!Enum.TryParse<SearchKind>(kindStr, out var kind))
            {
                continue;
            }

            if (!grouped.TryGetValue(kind, out var bucket))
            {
                bucket = [];
                grouped[kind] = bucket;
            }
            if (bucket.Count >= configuration.HitsPerKind)
            {
                continue;
            }

            string title = doc.Get(SearchConstants.FieldTitle) ?? string.Empty;
            string bodyText = doc.Get(SearchConstants.FieldBody) ?? string.Empty;
            string snippet;
            try
            {
                using var stream = analyzer.GetTokenStream(SearchConstants.FieldBody, bodyText);
                snippet = highlighter.GetBestFragment(stream, bodyText) ?? string.Empty;
            }
            catch
            {
                snippet = string.Empty;
            }
            if (string.IsNullOrEmpty(snippet))
            {
                // No highlight fragment: HTML-encode the raw body so it's safe to render.
                var fallback = bodyText.Length <= snippetMax ? bodyText : bodyText[..snippetMax];
                snippet = WebUtility.HtmlEncode(fallback);
            }

            var metadata = ParseMetadata(doc.Get(SearchConstants.FieldMetadata));
            bucket.Add(new SearchHit(
                Kind: kind,
                EntityId: Guid.Parse(doc.Get(SearchConstants.FieldEntityId)),
                Title: title,
                Snippet: snippet,
                Score: scoreDoc.Score,
                Metadata: metadata));
        }

        var ordered = new[] { SearchKind.Agent, SearchKind.TestSuite, SearchKind.AgentCall, SearchKind.Evaluator, SearchKind.TestCase }
            .SelectMany(k => grouped.TryGetValue(k, out var bucket) ? bucket : Enumerable.Empty<SearchHit>())
            .ToList();

        return new SearchResults(ordered);
    }

    /// <summary>
    /// Searches the entity ids asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> SearchEntityIdsAsync(
        Guid projectId,
        string query,
        SearchKind kind,
        int maxHits,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxHits <= 0)
        {
            return [];
        }

        var settings = await settingsResolver.GetOrDefaultsAsync(projectId, cancellationToken);
        if (!settings.Enabled || !settings.IndexedKinds.Contains(kind))
        {
            return [];
        }

        using var analyzer = new StandardAnalyzer(LuceneIndexWriter.Version);
        var parser = new MultiFieldQueryParser(
            LuceneIndexWriter.Version,
            [SearchConstants.FieldTitle, SearchConstants.FieldBody, SearchConstants.FieldBoostedBody],
            analyzer)
        {
            DefaultOperator = QueryParserBase.AND_OPERATOR,
            // Leading wildcards are required for substring matching of content tokens
            // (PrefixQueryRewriter wraps bare terms as *term*).
            AllowLeadingWildcard = true,
        };

        Query parsed;
        try
        {
            parsed = parser.Parse(PrefixQueryRewriter.Rewrite(query));
        }
        catch (ParseException)
        {
            try
            {
                parsed = parser.Parse(QuerySanitizer.Escape(query));
            }
            catch (ParseException)
            {
                return [];
            }
        }

        var combined = new BooleanQuery
        {
            { parsed, Occur.MUST },
            { new TermQuery(new Term(SearchConstants.FieldProjectId, projectId.ToString())), Occur.MUST },
            { new TermQuery(new Term(SearchConstants.FieldKind, kind.ToString())), Occur.MUST },
        };

        using var reader = writer.AcquireReader();
        var searcher = reader.Searcher;

        var topDocs = searcher.Search(combined, maxHits);
        var ids = new List<Guid>(topDocs.ScoreDocs.Length);
        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var entityIdStr = doc.Get(SearchConstants.FieldEntityId);
            if (Guid.TryParse(entityIdStr, out var id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    private static IReadOnlyDictionary<string, string> ParseMetadata(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<string, string>();
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ToString();
            }
            return dict;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Gets the recent asynchronously.
    /// </summary>
    public Task<SearchResults> GetRecentAsync(
        Guid projectId,
        IReadOnlyList<SearchKind> kinds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || kinds.Count == 0)
        {
            return Task.FromResult(new SearchResults([]));
        }

        using var reader = writer.AcquireReader();
        var searcher = reader.Searcher;
        var sort = new Sort(new SortField(SearchConstants.FieldCreatedAt, SortFieldType.INT64, reverse: true));

        // Query each kind independently and cap it at `limit`. The previous approach ran one shared
        // top-N window across all kinds sorted by createdAt, so a high-volume kind — traces are
        // written continuously by the ingestion proxy and retained for weeks — filled the whole
        // window and starved the low-volume kinds (agents, suites, evaluators), degrading the
        // recent feed to traces-only on any busy project (issue #232). Per-kind queries guarantee
        // each kind contributes up to `limit` of its own most-recent items regardless of trace volume.
        var ordered = kinds
            .SelectMany(kind => GetRecentForKind(searcher, projectId, kind, limit, sort))
            .ToList();

        return Task.FromResult(new SearchResults(ordered));
    }

    private static IEnumerable<SearchHit> GetRecentForKind(
        IndexSearcher searcher,
        Guid projectId,
        SearchKind kind,
        int limit,
        Sort sort)
    {
        var query = new BooleanQuery
        {
            { new TermQuery(new Term(SearchConstants.FieldProjectId, projectId.ToString())), Occur.MUST },
            { new TermQuery(new Term(SearchConstants.FieldKind, kind.ToString())), Occur.MUST },
        };

        var topDocs = searcher.Search(query, null, limit, sort);
        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            if (!Guid.TryParse(doc.Get(SearchConstants.FieldEntityId), out var entityId))
            {
                continue;
            }

            yield return new SearchHit(
                Kind: kind,
                EntityId: entityId,
                Title: doc.Get(SearchConstants.FieldTitle) ?? string.Empty,
                Snippet: string.Empty,
                Score: 0f,
                Metadata: ParseMetadata(doc.Get(SearchConstants.FieldMetadata)));
        }
    }
}
