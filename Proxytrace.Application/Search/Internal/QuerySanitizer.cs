using System.Text;

namespace Proxytrace.Application.Search.Internal;

internal static class QuerySanitizer
{
    private static readonly char[] Special =
        /// <summary>
        /// Escape.
        /// </summary>
        ['+', '-', '&', '|', '!', '(', ')', '{', '}', '[', ']', '^', '"', '~', ':', '\\', '/', '?'];

    public static string Escape(string input)
    {
        var sb = new StringBuilder(input.Length + 8);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '*' && i == input.Length - 1)
            {
                sb.Append(c);
                continue;
            }
            if (Array.IndexOf(Special, c) >= 0 || c == '*')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
