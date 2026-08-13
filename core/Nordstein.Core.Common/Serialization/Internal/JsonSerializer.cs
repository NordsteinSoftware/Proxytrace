using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nordstein.Core.Common.Serialization.Internal;

internal class JsonSerializer : ISerializer
{
    private readonly JsonSerializerOptions options;

    public JsonSerializer(IReadOnlyCollection<JsonConverter> converters)
    {
        options = new JsonSerializerOptions();
        foreach (JsonConverter converter in converters)
        {
            options.Converters.Add(converter);
        }
    }
    
    public async ValueTask<Stream> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default)
    {
        Stream stream = new MemoryStream();
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, obj, options, cancellationToken);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default) 
        => System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken);
}