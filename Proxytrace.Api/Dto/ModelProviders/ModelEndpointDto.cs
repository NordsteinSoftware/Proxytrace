namespace Proxytrace.Api.Dto.ModelProviders;

/// <summary>
/// Data transfer object representing a model endpoint.
/// </summary>
public record ModelEndpointDto(
    Guid Id,
    string ModelName,
    Guid ProviderId,
    string ProviderName,
    decimal? InputTokenCost,
    decimal? OutputTokenCost,
    // Cached-input price is auto-fetched from the LiteLLM catalog and surfaced read-only — it is not
    // part of the create/update pricing requests below.
    decimal? CachedInputTokenCost,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request payload for create model endpoint operations.
/// </summary>
public record CreateModelEndpointRequest(
    string ModelName,
    decimal? InputTokenCost,
    decimal? OutputTokenCost);

/// <summary>
/// Request payload for update model endpoint pricing operations.
/// </summary>
public record UpdateModelEndpointPricingRequest(
    decimal? InputTokenCost, decimal? OutputTokenCost);
