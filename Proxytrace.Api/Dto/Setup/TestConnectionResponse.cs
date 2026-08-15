namespace Proxytrace.Api.Dto.Setup;

/// <summary>
/// Response payload for test connection operations.
/// </summary>
public record TestConnectionResponse(
    bool Success,
    string? ErrorCode,
    int ModelCount,
    string? Error = null,
    Guid? ErrorId = null);
