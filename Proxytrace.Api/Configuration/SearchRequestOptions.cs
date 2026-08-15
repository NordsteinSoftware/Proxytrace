namespace Proxytrace.Api.Configuration;

/// <summary>
/// Request-validation bounds for the search API (query and snippet lengths).
/// </summary>
public sealed record SearchRequestOptions
{
    /// <summary>
    /// Gets or sets the min query length.
    /// </summary>
    public int MinQueryLength { get; init; } = 2;
    /// <summary>
    /// Gets or sets the max query length.
    /// </summary>
    public int MaxQueryLength { get; init; } = 200;
    /// <summary>
    /// Gets or sets the min snippet length.
    /// </summary>
    public int MinSnippetLength { get; init; } = 20;
    /// <summary>
    /// Gets or sets the max snippet length.
    /// </summary>
    public int MaxSnippetLength { get; init; } = 1000;

    /// <summary>
    /// Validates.
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
