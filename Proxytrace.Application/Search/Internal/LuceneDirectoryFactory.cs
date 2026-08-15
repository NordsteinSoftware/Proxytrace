using Lucene.Net.Store;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Directory = Lucene.Net.Store.Directory;

namespace Proxytrace.Application.Search.Internal;

internal interface ILuceneDirectoryFactory
{
    Directory Open();
}

internal sealed class LuceneDirectoryFactory : ILuceneDirectoryFactory
{
    private readonly SearchConfiguration configuration;
    private readonly IHostEnvironment? environment;
    private readonly ILogger<LuceneDirectoryFactory>? logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuceneDirectoryFactory"/> class.
    /// </summary>
    public LuceneDirectoryFactory(
        SearchConfiguration configuration,
        IHostEnvironment? environment = null,
        ILogger<LuceneDirectoryFactory>? logger = null)
    {
        this.configuration = configuration;
        this.environment = environment;
        this.logger = logger;
    }

    /// <summary>
    /// Opens.
    /// </summary>
    public Directory Open()
    {
        // No host environment registered (e.g. unit-test container) → in-memory index.
        // Also explicit ":memory:" opt-in.
        if (environment is null
            || string.Equals(configuration.IndexPath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return new RAMDirectory();
        }

        string path = Path.IsPathRooted(configuration.IndexPath)
            ? configuration.IndexPath
            : Path.Combine(environment.ContentRootPath, configuration.IndexPath);

        return FSDirectory.Open(EnsureWritable(path));
    }

    /// <summary>
    /// Returns <paramref name="path"/> if it can be created and written to; otherwise falls back to a
    /// temp directory. This keeps the app from failing to start when the content root is read-only or
    /// owned by another user (e.g. a host-owned bind mount), at the cost of a non-persistent index.
    /// </summary>
    private string EnsureWritable(string path)
    {
        if (TryProbe(path))
        {
            return path;
        }

        string fallback = Path.Combine(Path.GetTempPath(), "proxytrace", "searchindex");
        logger?.LogWarning(
            "Search index path '{Path}' is not writable; falling back to '{Fallback}'. " +
            "The index will not persist across restarts — mount a volume writable by the app user to fix this.",
            path, fallback);
        TryProbe(fallback);
        return fallback;
    }

    private static bool TryProbe(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            string probe = Path.Combine(path, ".write-probe");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
