using Proxytrace.Domain.ApiKey;

namespace Proxytrace.Api.Dto.ApiKeys;

/// <summary>
/// Data transfer object representing a api key.
/// </summary>
public record ApiKeyDto(
    Guid Id,
    string Name,
    string KeyPrefix,
    Guid ProjectId,
    string ProjectName,
    Guid ProviderId,
    string ProviderName,
    IReadOnlyList<ApiKeyScopes> Scopes,
    Guid OwnerId,
    string OwnerEmail,
    DateTimeOffset CreatedAt,
    // Populated only in the response to key creation — the plaintext is hashed at rest and cannot be
    // shown again. Null in every list/overview response.
    string? PlaintextKey = null);

/// <summary>
/// Request payload for create api key operations.
/// </summary>
public record CreateApiKeyRequest(
    string Name,
    Guid ProjectId,
    IReadOnlyList<ApiKeyScopes>? Scopes = null,
    Guid? UserId = null);
