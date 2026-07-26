using System.Text;

namespace Proxytrace.Serialization.Internal;

/// <summary>
/// Repairs JSON that a model stopped writing part-way through. A model that runs out of output
/// budget cuts off mid-token, so the document arrives with an unterminated string and unclosed
/// braces — invalid as a whole, even though everything before the cut is intact and usually carries
/// the fields that matter (an evaluator's <c>Score</c> ahead of its long <c>Reasoning</c> prose, for
/// instance). Discarding the answer over its tail throws away a usable verdict.
/// </summary>
internal static class TruncatedJsonRepair
{
    /// <summary>
    /// Returns progressively more aggressive repairs of <paramref name="json"/>, best first, for the
    /// caller to try in order — each is only a candidate, so the caller decides whether it parses.
    /// Empty when the document is already balanced (nothing suggests truncation).
    /// </summary>
    public static IEnumerable<string> Candidates(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            yield break;

        var closers = new Stack<char>();
        bool inString = false;
        bool escaped = false;
        int lastMemberBreak = -1;
        string lastMemberBreakClosers = string.Empty;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    closers.Push('}');
                    break;
                case '[':
                    closers.Push(']');
                    break;
                case '}':
                case ']':
                    if (closers.Count > 0)
                        closers.Pop();
                    break;
                case ',':
                    // The last point at which the document was structurally complete apart from its
                    // open containers — where the second candidate cuts back to. A Stack enumerates
                    // top-down, which is exactly the order the containers have to be closed in.
                    lastMemberBreak = i;
                    lastMemberBreakClosers = new string(closers.ToArray());
                    break;
            }
        }

        if (!inString && closers.Count == 0)
            yield break;

        // Close what the model left open. Enough whenever the cut fell inside a value — the common
        // case, since the longest field is the one that runs out of room.
        var closed = new StringBuilder(json);
        if (inString)
        {
            // A trailing lone backslash would escape the quote we are about to add.
            if (escaped)
                closed.Length--;
            closed.Append('"');
        }

        foreach (char closer in closers)
            closed.Append(closer);

        yield return closed.ToString();

        // The cut fell in a place closing cannot rescue (mid-key, after a colon, inside a partial
        // escape sequence). Drop the incomplete member entirely and keep the ones before it.
        if (lastMemberBreak >= 0)
            yield return json[..lastMemberBreak] + lastMemberBreakClosers;
    }
}
