using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Proxytrace.Domain.Message;

/// <summary>
/// Compares an <b>expected</b> tool request against an <b>actual</b> one for evaluation.
///
/// Deliberately ignores <see cref="ToolRequest.Id"/>: an expected output's ids are minted locally
/// when the case is stored, while the actual ids come from the provider, so an id-sensitive
/// comparison could never match. Arguments are compared as canonical JSON (key order- and
/// whitespace-insensitive, numbers by value) and fall back to trimmed string equality when either
/// side is not JSON.
/// </summary>
public static class ToolRequestMatch
{
    /// <summary>True when both requests name the same tool with equivalent arguments.</summary>
    public static bool Matches(ToolRequest expected, ToolRequest actual)
        => string.Equals(expected.Name, actual.Name, StringComparison.Ordinal)
           && ArgumentsMatch(expected.Arguments, actual.Arguments);

    /// <summary>
    /// Human-readable differences between the expected and actual tool calls, compared as an
    /// UNORDERED multiset — parallel tool calls carry no meaningful order, and the same two calls
    /// emitted the other way round is not a defect. An empty result means they match.
    /// </summary>
    public static IReadOnlyList<string> Differences(
        IReadOnlyList<ToolRequest> expected,
        IReadOnlyList<ToolRequest> actual)
    {
        List<ToolRequest> unmatched = [.. actual];
        List<string> differences = [];

        foreach (ToolRequest want in expected)
        {
            int exact = unmatched.FindIndex(candidate => Matches(want, candidate));
            if (exact >= 0)
            {
                unmatched.RemoveAt(exact);
                continue;
            }

            // Same tool, different arguments is the interesting failure — report both sides rather
            // than the useless pair "expected X, never called" + "unexpected X".
            int sameName = unmatched.FindIndex(
                candidate => string.Equals(want.Name, candidate.Name, StringComparison.Ordinal));
            if (sameName >= 0)
            {
                differences.Add($"Expected tool '{Describe(want)}' but got '{Describe(unmatched[sameName])}'");
                unmatched.RemoveAt(sameName);
                continue;
            }

            differences.Add($"Expected tool '{want.Name}' but it was not called");
        }

        differences.AddRange(unmatched.Select(extra => $"Unexpected tool '{Describe(extra)}'"));
        return differences;
    }

    private static string Describe(ToolRequest request)
        => $"{request.Name}({request.Arguments.Trim()})";

    /// <summary>"40.0" → "40", "0.50" → "0.5", "100" → "100" (an integer has nothing to trim).</summary>
    private static string TrimTrailingZeros(string number)
        => number.Contains('.', StringComparison.Ordinal)
            ? number.TrimEnd('0').TrimEnd('.')
            : number;

    private static bool ArgumentsMatch(string expected, string actual)
        => TryCanonicalize(expected, out string? canonicalExpected)
           && TryCanonicalize(actual, out string? canonicalActual)
            ? string.Equals(canonicalExpected, canonicalActual, StringComparison.Ordinal)
            : string.Equals(expected.Trim(), actual.Trim(), StringComparison.Ordinal);

    private static bool TryCanonicalize(string json, out string? canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteCanonical(document.RootElement, writer);
            }
            canonical = Encoding.UTF8.GetString(buffer.ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                // Normalize 40 / 40.0 / 4e1 to one representation. Note decimal PRESERVES scale —
                // 40m and 40.0m are distinct representations and write differently — so the
                // trailing zeros have to go explicitly. A number too large or precise for decimal
                // keeps its raw text: still stable, just not value-normalized.
                if (element.TryGetDecimal(out decimal number))
                {
                    writer.WriteRawValue(TrimTrailingZeros(number.ToString(CultureInfo.InvariantCulture)));
                }
                else
                {
                    writer.WriteRawValue(element.GetRawText());
                }
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
