using System.Text.Json;
using Proxytrace.Api.Dto.Agents;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Api.Dto.Tools;

/// <summary>
/// Canonical mapping from <see cref="ToolSpecification"/> / <see cref="IToolArgument"/>
/// to their wire DTOs. Shared by every controller that surfaces tool definitions.
/// </summary>
public sealed class ToolDtoMapper
{
    public ToolSpecificationDto ToToolSpecDto(ToolSpecification t)
        => new(t.Name, t.Description, [.. t.Arguments.Arguments.Select(ToToolArgumentDto)]);

    public ToolArgumentDto ToToolArgumentDto(IToolArgument arg)
    {
        var (type, enumValues) = ParseJsonSchema(arg.JsonSchema);
        return new ToolArgumentDto(arg.Name, arg.Description, type, arg.IsRequired, enumValues);
    }

    private static (string Type, List<string>? EnumValues) ParseJsonSchema(string jsonSchema)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonSchema);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString() ?? "object"
                : "object";
            List<string>? enumValues = null;
            if (root.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
                enumValues = [.. enumEl.EnumerateArray().Select(e => e.GetString() ?? "")];
            return (type, enumValues);
        }
        catch
        {
            return ("object", null);
        }
    }
}
