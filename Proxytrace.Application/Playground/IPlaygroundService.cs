using Proxytrace.Application.Playground.Internal;

namespace Proxytrace.Application.Playground;

/// <summary>
/// Executes a one-off prompt against a chosen agent and endpoint, streaming tokens back as they arrive.
/// </summary>
public interface IPlaygroundService
{
    /// <summary>
    /// Streams the model's response tokens for the given playground request until generation completes or cancellation is requested.
    /// </summary>
    IAsyncEnumerable<PlaygroundEvent> CompleteStreamAsync(
        PlaygroundCompleteRequest request,
        CancellationToken cancellationToken);
}
