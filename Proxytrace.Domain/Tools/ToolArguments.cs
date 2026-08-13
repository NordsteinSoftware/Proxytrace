using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Nordstein.Core.Common.Validation;

namespace Proxytrace.Domain.Tools
{
    /// <summary>
    /// The arguments a tool accepts
    /// </summary>
    public sealed record ToolArguments :
        IDomainObject,
        IEnumerable<IToolArgument>
    {
        /// <summary>
        /// No arguments
        /// </summary>
        public static readonly ToolArguments None
            = new();

        /// <summary>
        /// The arguments the tool accepts
        /// </summary>
        public IReadOnlyList<IToolArgument> Arguments { get; }

        /// <summary>
        /// The JSON schema describing the arguments the tool accepts
        /// </summary>
        public string JsonSchema { get; }

        /// <summary>
        /// The number of arguments
        /// </summary>
        public int Count
            => Arguments.Count;

        /// <summary>
        /// Indexer to get argument metadata by index
        /// </summary>
        public IToolArgument this[int index]
            => Arguments[index];

        /// <summary>
        /// Creates a new instance of <see cref="ToolArguments"/>
        /// </summary>
        /// <param name="arguments"></param>
        public ToolArguments(params IReadOnlyList<IToolArgument> arguments)
        {
            Arguments = arguments;
            JsonSchema = ToJsonSchema(arguments);
        }

        /// <inheritdoc />
        public bool Equals(ToolArguments? other)
            => other is not null
               && JsonSchema == other.JsonSchema
               && Arguments.SequenceEqual(other.Arguments);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // Fold the arguments' elements (Equals uses SequenceEqual); hashing the list reference
            // would give equal-content instances different hashes and break the
            // Equals/GetHashCode contract — the same idiom as Message/Conversation/ModelParameters.
            var hash = new HashCode();
            hash.Add(JsonSchema);
            foreach (IToolArgument argument in Arguments)
            {
                hash.Add(argument);
            }
            return hash.ToHashCode();
        }

        /// <summary>
        /// Generates a JSON schema from the provided tool argument metadata
        /// </summary>
        private static string ToJsonSchema(IReadOnlyCollection<IToolArgument> argumentMetadata)
        {
            // Create the properties object for the JSON schema
            var properties = new Dictionary<string, object?>();
            var required = new List<string>();

            foreach (IToolArgument metadata in argumentMetadata)
            {
                // ignore CancellationToken parameters
                if (metadata.Type == typeof(CancellationToken))
                {
                    continue;
                }

                // Deserialize the JSON schema string to an object to avoid double-serialization
                properties[metadata.Name] = JsonSerializer.Deserialize<object>(metadata.JsonSchema);

                // Add to required list if the argument is required
                if (metadata.IsRequired)
                {
                    required.Add(metadata.Name);
                }
            }

            var schema = new
            {
                type = "object",
                properties,
                required = required.ToArray()
            };

            var schemaString = JsonSerializer.Serialize(schema, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            return schemaString;
        }

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            => Validation.Json(JsonSchema).AsEnumerable()
                .Concat(Arguments.SelectMany(argument => argument.Validate(validationContext)));

        /// <inheritdoc />
        public IEnumerator<IToolArgument> GetEnumerator()
            => Arguments.GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        /// <summary>
        /// Parses a JSON schema string into a <see cref="ToolArguments"/> instance
        /// </summary>
        public static ToolArguments FromJsonSchema(string jsonSchemaString)
        {
            using JsonDocument document = JsonDocument.Parse(jsonSchemaString);
            return FromJsonSchema(document.RootElement);
        }

        /// <summary>
        /// Parses a JSON schema into a <see cref="ToolArguments"/> instance
        /// </summary>
        public static ToolArguments FromJsonSchema(JsonElement jsonSchema)
        {
            // Get the properties object
            if (!jsonSchema.TryGetProperty("properties", out JsonElement properties))
            {
                return None;
            }

            // Get the required array (if it exists)
            var requiredFields = new HashSet<string>();
            if (jsonSchema.TryGetProperty("required", out JsonElement required)
                && required.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in required.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        requiredFields.Add(item.GetString() ?? string.Empty);
                    }
                }
            }

            IReadOnlyList<JsonToolArgument> arguments = properties.EnumerateObject().Select(property =>
            {
                var name = property.Name;
                var isRequired = requiredFields.Contains(name);
                return new JsonToolArgument(name, isRequired, property.Value);
            }).ToList();
            
            ToolArguments result = new ToolArguments(arguments);
            result.Validate();
            return result;
        }

    }
}