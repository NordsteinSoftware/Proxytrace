using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proxytrace.Api.Json;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used for server-sent event payloads
/// across the API controllers.
/// </summary>
internal static class ApiJsonOptions
{
    /// <summary>
    /// The sse.
    /// </summary>
    public static readonly JsonSerializerOptions Sse = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
