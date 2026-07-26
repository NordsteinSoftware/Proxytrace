using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Proxytrace.Common.Validation;

namespace Proxytrace.Serialization.Internal;

/// <inheritdoc />
internal record JsonOutputFormat : IOutputFormat
{
    internal delegate JsonOutputFormat Create(Type type);
    
    private readonly ISerializer serializer;

    /// <summary>
    /// The JSON schema definition that the output must adhere to
    /// </summary>
    public string Schema { get; }

    public JsonOutputFormat(
        Type type, 
        ISerializer serializer)
    {
        this.serializer = serializer;
        
        JsonSerializerOptions jsonSerializerOptions = new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            PropertyNameCaseInsensitive = true
        };
        jsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        
        JsonSchemaExporterOptions jsonSchemaExporterOptions1 = new()
        {
            TreatNullObliviousAsNonNullable = false,
            TransformSchemaNode = (context, schema) =>
            {
                if (context.TypeInfo.Type.IsEnum)
                {
                    var enumNames = Enum.GetNames(context.TypeInfo.Type);
                    var enumArray = new JsonArray();
                    foreach (var enumName in enumNames)
                    {
                        enumArray.Add(JsonNamingPolicy.CamelCase.ConvertName(enumName));
                    }
                    schema["enum"] = enumArray;
                }

                var descriptionAttribute = context.PropertyInfo?.AttributeProvider
                    ?.GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
                    .OfType<DescriptionAttribute>()
                    .FirstOrDefault();

                if (descriptionAttribute != null && !string.IsNullOrWhiteSpace(descriptionAttribute.Description))
                {
                    schema["description"] = descriptionAttribute.Description;
                }

                return schema;
            }
        };
        
        Schema = jsonSerializerOptions.GetJsonSchemaAsNode(
            type,
            jsonSchemaExporterOptions1).ToString();
    }

    /// <inheritdoc />
    public string ToPromptString()
        => $"Respond only in JSON format that adheres to the following JSON schema definition:\n{Schema}";
    
    public async Task<TOutput?> ParseAsync<TOutput>(string? output, CancellationToken cancellationToken = default)
    {
        string? cleanedOutput = TrimArtifacts(output);
        if (cleanedOutput is null)
            return default;

        try
        {
            var deserialized = await serializer.DeserializeAsync<TOutput>(cleanedOutput, cancellationToken);
            return deserialized
                   ?? throw new InvalidOperationException($"Failed to deserialize model output to {typeof(TOutput).FullName}");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A model that runs out of output budget stops mid-token, leaving an unterminated string
            // and unclosed braces. Everything written before the cut is still good — and the fields
            // that carry the answer are usually written before the long prose that ran out of room —
            // so close what was left open and parse that rather than losing the whole response.
            TOutput? salvaged = await TryParseTruncatedAsync<TOutput>(cleanedOutput, cancellationToken);
            if (salvaged is not null)
                return salvaged;

            throw;
        }
    }

    private async Task<TOutput?> TryParseTruncatedAsync<TOutput>(string output, CancellationToken cancellationToken)
    {
        foreach (string candidate in TruncatedJsonRepair.Candidates(output))
        {
            try
            {
                var deserialized = await serializer.DeserializeAsync<TOutput>(candidate, cancellationToken);
                if (deserialized is not null)
                    return deserialized;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // This repair didn't parse either — try the next, more aggressive one.
            }
        }

        return default;
    }
    
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        yield return Validation.NotNullOrWhiteSpace(Schema);
        yield return Validation.Json(Schema);
    }
    
    private string? TrimArtifacts(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        string cleaned = output.Trim();

        if (cleaned.StartsWith("```"))
        {
            int firstNewline = cleaned.IndexOf('\n');
            cleaned = firstNewline > 0
                ? new string(cleaned.Skip(firstNewline + 1).ToArray()).Trim()
                : new string(cleaned.Skip(3).ToArray()).Trim();
        }

        if (cleaned.EndsWith("```"))
            cleaned = cleaned[..^3].Trim();

        string[] commonPrefixes = ["json:", "Output:", "Result:", "Response:"];
        foreach (string prefix in commonPrefixes)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..].Trim();
                break;
            }
        }

        cleaned = cleaned.Trim('\"', '\'');

        return string.Equals(cleaned, "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : cleaned;
    }
}