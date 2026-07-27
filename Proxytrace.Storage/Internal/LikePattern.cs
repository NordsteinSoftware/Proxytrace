namespace Proxytrace.Storage.Internal;

/// <summary>
/// Builds the right-hand side of an <c>EF.Functions.Like</c> infix search.
/// </summary>
/// <remarks>
/// <para>
/// Two things go wrong with a hand-rolled <c>$"%{search}%"</c>:
/// </para>
/// <list type="number">
/// <item><b>Case.</b> SQL <c>LIKE</c> is case-sensitive on PostgreSQL but the EF in-memory provider
/// matches case-insensitively — so a test passes while production silently misses matches. Callers
/// must lower <b>both</b> sides: pass the column through <c>.ToLower()</c> and the search term
/// through <see cref="Contains"/>, which lowers it here.</item>
/// <item><b>Wildcards.</b> <c>%</c> and <c>_</c> typed by the user are operators, not literals, so
/// searching for <c>100%</c> or <c>a_b</c> matches far more than the user asked for. They are
/// escaped here under <see cref="EscapeCharacter"/>, which the caller must pass to the matching
/// <c>EF.Functions.Like</c> overload.</item>
/// </list>
/// <para>
/// Usage — the column is lowered in the query, the term is lowered and escaped here:
/// <code>
/// var pattern = LikePattern.Contains(search);
/// query = query.Where(e => EF.Functions.Like(e.Name.ToLower(), pattern, LikePattern.EscapeCharacter));
/// </code>
/// </para>
/// </remarks>
internal static class LikePattern
{
    /// <summary>
    /// Escape character for the generated patterns. Pass to the three-argument
    /// <c>EF.Functions.Like(matchExpression, pattern, escapeCharacter)</c> overload.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>
    /// Builds a case-insensitive "contains" pattern for <paramref name="search"/>: trims it, lowers
    /// it, and escapes the <c>LIKE</c> wildcards so user input is matched literally.
    /// </summary>
    public static string Contains(string search)
        => $"%{Escape(search.Trim().ToLowerInvariant())}%";

    // The escape character itself must be escaped first, otherwise escaping a wildcard would
    // re-escape the backslash this method just introduced.
    private static string Escape(string value)
        => value
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
}
