using Proxytrace.Application.Playground.Internal;

namespace Proxytrace.Application.Playground;

/// <summary>
/// Service that provides playground functionality.
/// </summary>
public interface IPlaygroundService
{
    IAsyncEnumerable<PlaygroundEvent> CompleteStreamAsync(
        PlaygroundCompleteRequest request,
        CancellationToken cancellationToken);
}
