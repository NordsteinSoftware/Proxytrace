namespace Proxytrace.Api.Configuration;

/// <summary>
/// Request-validation bounds for the search API (query and snippet lengths).
/// </summary>
public sealed record SearchRequestOptions
{
    /// <summary>
    /// Minimum number of characters a search query must contain before the request is accepted.
    /// </summary>
    public int MinQueryLength { get; init; } = 2;
    /// <summary>
    /// Maximum number of characters a search query may contain; requests exceeding this are rejected
    /// with a 400.
    /// </summary>
    public int MaxQueryLength { get; init; } = 200;
    /// <summary>
    /// Minimum character length of a snippet submitted for snippet-search indexing.
    /// </summary>
    public int MinSnippetLength { get; init; } = 20;
    /// <summary>
    /// Maximum character length of a snippet; snippets longer than this are rejected with a 400.
    /// </summary>
    public int MaxSnippetLength { get; init; } = 1000;

    /// <summary>
    /// Asserts that the configured bounds are internally consistent; throws
    /// <see cref="InvalidOperationException"/> on startup when they are not.
    /// </summary>
    public void Validate()
    {
        if (MinQueryLength < 1 || MinQueryLength > MaxQueryLength)
        {
            throw new InvalidOperationException(
                $"{nameof(SearchRequestOptions)}: {nameof(MinQueryLength)} must be >= 1 and <= {nameof(MaxQueryLength)}.");
        }

        if (MinSnippetLength < 1 || MinSnippetLength > MaxSnippetLength)
        {
            throw new InvalidOperationException(
                $"{nameof(SearchRequestOptions)}: {nameof(MinSnippetLength)} must be >= 1 and <= {nameof(MaxSnippetLength)}.");
        }
    }
}
