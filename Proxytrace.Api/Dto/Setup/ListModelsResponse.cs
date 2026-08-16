namespace Proxytrace.Api.Dto.Setup;

/// <summary>
/// Response payload for list models operations.
/// </summary>
public record ListModelsResponse(IReadOnlyList<string> Models);
