namespace Proxytrace.Api.Dto.Setup;

/// <summary>
/// Response payload for complete setup operations.
/// </summary>
public record CompleteSetupResponse(
    Guid ProviderId,
    Guid EndpointId,
    Guid ProjectId);
