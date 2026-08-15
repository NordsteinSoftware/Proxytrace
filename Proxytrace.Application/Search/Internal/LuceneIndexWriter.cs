using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Directory = Lucene.Net.Store.Directory;

namespace Proxytrace.Application.Search.Internal;

internal sealed class LuceneIndexWriter : IDisposable
{
    /// <summary>
    /// The version constant value.
    /// </summary>
    public const LuceneVersion Version = LuceneVersion.LUCENE_48;

    private readonly Directory directory;
    private readonly bool ownsDirectory;
    private readonly IndexWriter writer;
    private readonly SearcherManager searcherManager;

    // Deliberate exception to the "always IAsyncLock" rule (see docs/code-style.md §Concurrency):
    // this guards purely-synchronous Lucene IndexWriter operations with no await in the critical
    // section, so System.Threading.Lock is the correct primitive — IAsyncLock buys no safety here.
    private readonly Lock commitLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LuceneIndexWriter"/> class.
    /// </summary>
    public LuceneIndexWriter(ILuceneDirectoryFactory factory) : this(factory.Open(), ownsDirectory: true)
    {
    }

    private LuceneIndexWriter(Directory directory, bool ownsDirectory)
    {
        this.directory = directory;
        this.ownsDirectory = ownsDirectory;
        var analyzer = new StandardAnalyzer(Version);
        var config = new IndexWriterConfig(Version, analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND,
        };
        writer = new IndexWriter(directory, config);
        writer.Commit();
        searcherManager = new SearcherManager(writer, applyAllDeletes: true, null);
    }

    internal static LuceneIndexWriter ForTesting(Directory directory)
        => new(directory, ownsDirectory: false);

    /// <summary>
    /// Upsert.
    /// </summary>
    public void Upsert(string id, Document doc)
    {
        lock (commitLock)
        {
            writer.UpdateDocument(new Term(SearchConstants.FieldId, id), doc);
            writer.Commit();
            searcherManager.MaybeRefresh();
        }
    }

    /// <summary>
    /// Deletes.
    /// </summary>
    public void Delete(string id)
    {
        lock (commitLock)
        {
            writer.DeleteDocuments(new Term(SearchConstants.FieldId, id));
            writer.Commit();
            searcherManager.MaybeRefresh();
        }
    }

    /// <summary>
    /// Deletes the by query.
    /// </summary>
    public void DeleteByQuery(Query query)
    {
        lock (commitLock)
        {
            writer.DeleteDocuments(query);
            writer.Commit();
            searcherManager.MaybeRefresh();
        }
    }

    /// <summary>
    /// Upsert deferred.
    /// </summary>
    public void UpsertDeferred(string id, Document doc)
    {
        lock (commitLock)
        {
            writer.UpdateDocument(new Term(SearchConstants.FieldId, id), doc);
        }
    }

    /// <summary>
    /// Deletes the deferred.
    /// </summary>
    public void DeleteDeferred(string id)
    {
        lock (commitLock)
        {
            writer.DeleteDocuments(new Term(SearchConstants.FieldId, id));
        }
    }

    /// <summary>
    /// Commits the and refresh.
    /// </summary>
    public void CommitAndRefresh()
    {
        lock (commitLock)
        {
            writer.Commit();
            searcherManager.MaybeRefresh();
        }
    }

    /// <summary>
    /// Acquires the reader.
    /// </summary>
    public AcquiredReader AcquireReader()
    {
        lock (commitLock)
        {
            searcherManager.MaybeRefresh();
            var searcher = searcherManager.Acquire();
            return new AcquiredReader(searcherManager, searcher);
        }
    }

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        searcherManager.Dispose();
        writer.Dispose();
        if (ownsDirectory)
        {
            directory.Dispose();
        }
    }

    internal sealed class AcquiredReader : IDisposable
    {
        private readonly SearcherManager manager;
        private readonly IndexSearcher searcher;

        /// <summary>
        /// Initializes a new instance of the <see cref="AcquiredReader"/> class.
        /// </summary>
        public AcquiredReader(SearcherManager manager, IndexSearcher searcher)
        {
            this.manager = manager;
            this.searcher = searcher;
        }

        /// <summary>
        /// Gets the searcher.
        /// </summary>
        public IndexSearcher Searcher => searcher;
        /// <summary>
        /// Gets the index reader.
        /// </summary>
        public IndexReader IndexReader => searcher.IndexReader;

        /// <summary>
        /// Releases all resources used by the current instance.
        /// </summary>
        public void Dispose() => manager.Release(searcher);
    }
}
