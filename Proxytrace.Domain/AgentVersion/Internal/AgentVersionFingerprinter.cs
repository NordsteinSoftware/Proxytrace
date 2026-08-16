using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.AgentVersion.Internal;

internal sealed class AgentVersionFingerprinter : IAgentVersionFingerprinter
{
    // Tool JSON schemas are highly repetitive across versions; cache the stripped form to skip the
    // parse+rewrite round-trip on hot ingestion paths. Bounded by the number of unique tool schemas
    // in the project, which is small in practice.
    private readonly ConcurrentDictionary<string, string> strippedSchemaCache = new();
    /// <summary>
    /// Strict.
    /// </summary>
    public string Strict(IPromptTemplate systemPrompt, IReadOnlyCollection<ToolSpecification> tools)
    {
        var sb = new StringBuilder();
        sb.Append(systemPrompt.Template).Append('\0').Append('\0');
        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            sb.Append(tool.Name).Append('\0');
            sb.Append(tool.Description).Append('\0');
            sb.Append(tool.Arguments.JsonSchema).Append('\0');
        }
        return Hash(sb);
    }

    /// <summary>
    /// Loose.
    /// </summary>
    public string Loose(IPromptTemplate systemPrompt, IReadOnlyCollection<ToolSpecification> tools)
    {
        var sb = new StringBuilder();
        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            sb.Append(tool.Name).Append('\0');
            sb.Append(strippedSchemaCache.GetOrAdd(tool.Arguments.JsonSchema, StripDescriptions)).Append('\0');
        }
        return Hash(sb);
    }

    private static string Hash(StringBuilder sb)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Recursively removes all "description" properties from a JSON schema string.
    /// Returns the input unchanged if it doesn't parse as JSON.
    /// </summary>
    private static string StripDescriptions(string jsonSchema)
    {
        if (string.IsNullOrWhiteSpace(jsonSchema))
        {
            return jsonSchema;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonSchema);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteStripped(doc.RootElement, writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return jsonSchema;
        }
    }

    private static void WriteStripped(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "description", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    writer.WritePropertyName(prop.Name);
                    WriteStripped(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteStripped(item, writer);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
