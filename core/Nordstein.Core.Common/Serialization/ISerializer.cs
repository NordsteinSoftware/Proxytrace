using JetBrains.Annotations;

namespace Nordstein.Core.Common.Serialization;

public interface ISerializer
{
    [MustDisposeResource]
    public ValueTask<Stream> SerializeAsync<T>(T obj, CancellationToken cancellationToken = default);
    
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default);
}