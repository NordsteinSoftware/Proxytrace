using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Serialization.Internal;

    /// <inheritdoc />
internal class JsonSerializer : ISerializer
{
    private readonly JsonSerializerOptions serializerOptions;
    
    public JsonSerializer() : this([]) { }

    public JsonSerializer(IEnumerable<JsonConverter> converters)
    {
        JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        jsonSerializerOptions.Converters.Add(new ObjectToInferredTypesConverter());
        jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));

        foreach (var converter in converters)
        {
            jsonSerializerOptions.Converters.Add(converter);
        }

        serializerOptions = jsonSerializerOptions;
    }
    
    /// <inheritdoc />
    public string Serialize(object? input, bool writeIndented = false)
    {
        if (input is null)
        {
            return string.Empty;
        }

        JsonSerializerOptions options = new JsonSerializerOptions(serializerOptions)
        {
            WriteIndented = writeIndented
        };
        // UnsafeRelaxedJsonEscaping (set on the options above) already emits non-ASCII
        // characters unescaped, so no post-processing is needed. The previous \uXXXX
        // un-escaping pass corrupted output by turning mandatory control-character escapes
        // (e.g. a newline escape inside a string) into raw control characters, producing invalid JSON.
        return System.Text.Json.JsonSerializer.Serialize(input, input.GetType(), options);
    }

    /// <inheritdoc />
    public async Task<TOutput?> DeserializeAsync<TOutput>(string value, CancellationToken cancellationToken = default)
    {
        try
        {
            return typeof(TOutput) switch
            {
                var t when t == typeof(string) =>
                    (TOutput)(object)value,
                var t when t == typeof(Guid) =>
                    (TOutput?)(object?)ParseGuid(value),
                _ => await ParseObject<TOutput>(value, cancellationToken)

            };
        }
        catch(Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }
            throw new SerializationException($"Failed to deserialize model output to {typeof(TOutput).FullName} from {value}", ex);
        }
    }

    /// <inheritdoc />
    public TOutput? Deserialize<TOutput>(string value)
    {
        if(typeof(TOutput) == typeof(string))
        {
            return (TOutput)(object)value;
        }
        
        TOutput? deserialized = System.Text.Json.JsonSerializer.Deserialize<TOutput>(value, serializerOptions);
        if (deserialized is IValidatableObject validatable)
        {
            validatable.Validate();
        }
        return deserialized;
    }

    private async Task<TOutput?> ParseObject<TOutput>(string data, CancellationToken cancellationToken)
    {
        using MemoryStream outputStream = new(System.Text.Encoding.UTF8.GetBytes(data));
        TOutput? deserialized = await System.Text.Json.JsonSerializer.DeserializeAsync<TOutput>(outputStream, serializerOptions,
            cancellationToken: cancellationToken);

        if (deserialized is IValidatableObject validatable)
        {
            validatable.Validate();
        }

        return deserialized;
    }

    private Guid? ParseGuid(string data) 
        => Guid.TryParse(data, out Guid result) 
            ? result 
            : null;
}