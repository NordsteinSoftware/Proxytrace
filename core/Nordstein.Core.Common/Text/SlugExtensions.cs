using System.Text;

namespace Nordstein.Core.Common.Text;

/// <summary>
/// Derives URL-safe slugs from human-readable names.
/// </summary>
public static class SlugExtensions
{
    /// <summary>
    /// Converts a name into a URL slug: lower-cased, non-alphanumeric characters dropped, and
    /// runs of whitespace collapsed into single hyphens (e.g. "Showcase Project" → "showcase-project").
    /// </summary>
    public static string ToSlug(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        var pendingHyphen = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingHyphen = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
            {
                // Defer the separator so leading/trailing/duplicate separators collapse away.
                pendingHyphen = true;
            }
        }

        return builder.ToString();
    }
}
