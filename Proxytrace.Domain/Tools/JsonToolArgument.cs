using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Domain.Tools;

/// <summary>
/// A tool argument defined by a JSON schema.
/// </summary>
internal sealed record JsonToolArgument : IToolArgument
{
    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public bool IsRequired { get; }

    /// <inheritdoc />
    public Type Type
        => typeof(object);
    
    /// <inheritdoc />
    public string? Description { get; }

    /// <inheritdoc />
    public object? DefaultValue
        => null;

    /// <inheritdoc />
    public string JsonSchema { get; }

    /// <summary>
    /// Creates a new instance of <see cref="JsonToolArgument"/>
    /// </summary>
    public JsonToolArgument(string name, bool isRequired, JsonElement json)
    {
        Name = name;
        IsRequired = isRequired;
        JsonSchema = json.GetRawText();
        
        if (json.TryGetProperty("description", out JsonElement descriptionElement)
            && descriptionElement.ValueKind == JsonValueKind.String)
        {
            Description = descriptionElement.GetString();
        }
    }
    
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotNullOrWhiteSpace(Name);
        yield return Validation.NotNullOrWhiteSpace(JsonSchema);
        yield return Validation.Json(JsonSchema);
    }
}