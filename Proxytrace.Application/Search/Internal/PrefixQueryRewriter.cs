using System.Text;

namespace Proxytrace.Application.Search.Internal;

internal static class PrefixQueryRewriter
{
    private static readonly HashSet<string> ReservedWords = ["AND", "OR", "NOT", "TO"];

    /// <summary>
    /// Rewrite.
    /// </summary>
    public static string Rewrite(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length + 8);
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c))
            {
                sb.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                sb.Append(c);
                i++;
                while (i < input.Length && input[i] != '"')
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        sb.Append(input[i]);
                        i++;
                    }
                    sb.Append(input[i]);
                    i++;
                }
                if (i < input.Length)
                {
                    sb.Append(input[i]);
                    i++;
                }
                continue;
            }

            if (IsLeadingOperator(c))
            {
                sb.Append(c);
                i++;
                continue;
            }

            if (IsStandaloneOperator(c))
            {
                sb.Append(c);
                i++;
                continue;
            }

            int start = i;
            while (i < input.Length && !IsTokenBoundary(input[i]))
            {
                i++;
            }
            var token = input[start..i];
            sb.Append(WrapWildcardIfEligible(token));
        }

        return sb.ToString();
    }

    private static bool IsLeadingOperator(char c) => c is '+' or '-' or '!';

    private static bool IsStandaloneOperator(char c) =>
        c is '(' or ')' or '[' or ']' or '{' or '}' or '&' or '|' or '^' or '~' or '\\';

    private static bool IsTokenBoundary(char c) =>
        char.IsWhiteSpace(c) || c is '"' or '(' or ')' or '[' or ']' or '{' or '}' or '&' or '|' or '^' or '~' or '\\';

    // Wrap bare tokens with leading AND trailing wildcards so a query word matches an indexed
    // term anywhere it occurs (substring), not just as a prefix. Captured trace content is full
    // of compound/identifier tokens (e.g. "search_users", "getUserData") that the analyzer keeps
    // whole; prefix-only matching missed them when the user typed an interior fragment.
    private static string WrapWildcardIfEligible(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }
        if (ReservedWords.Contains(token))
        {
            return token;
        }
        if (ContainsWildcard(token))
        {
            return token;
        }

        int colon = token.IndexOf(':');
        if (colon == token.Length - 1)
        {
            return token;
        }
        if (colon > 0)
        {
            var field = token[..(colon + 1)];
            var value = token[(colon + 1)..];
            if (ContainsWildcard(value))
            {
                return token;
            }
            return field + "*" + value + "*";
        }

        return "*" + token + "*";
    }

    private static bool ContainsWildcard(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '*' || s[i] == '?')
            {
                return true;
            }
        }
        return false;
    }
}
