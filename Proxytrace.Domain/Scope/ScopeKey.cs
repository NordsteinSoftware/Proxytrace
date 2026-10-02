using Nordstein.Core.Common.Text;

namespace Proxytrace.Domain.Scope;

/// <summary>
/// Canonical form of a scope's external key — the value a client sends as the
/// <c>/{project}/{scope}/openai/v1</c> path segment or the <c>x-proxytrace-scope</c> header. Keys
/// follow the project-slug rules so a scope reads the same in a URL, a header and the UI:
/// "Support Agents", "support_agents" and "support-agents" are one scope.
/// </summary>
public static class ScopeKey
{
    /// <summary>The longest canonical key; longer input is truncated.</summary>
    public const int MaxLength = 64;

    // Path segments of the proxy's own route surface. A scope named like one of them would make a
    // scoped URL read ambiguously (`/acme/openai/openai/v1`), so they never become scopes.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "openai", "v1" };

    /// <summary>
    /// Normalizes a raw client-supplied key into its canonical slug, or returns <see langword="null"/>
    /// when nothing usable is left (empty, punctuation only, or a reserved word). Idempotent:
    /// <c>Normalize(Normalize(x)) == Normalize(x)</c>, so the proxy and ingestion can both apply it.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var slug = raw.ToSlug();
        if (slug.Length > MaxLength)
            slug = slug[..MaxLength].TrimEnd('-');

        return slug.Length == 0 || Reserved.Contains(slug) ? null : slug;
    }

    /// <summary>Whether <paramref name="key"/> is already in canonical form.</summary>
    public static bool IsCanonical(string? key)
        => key is not null && Normalize(key) == key;
}
