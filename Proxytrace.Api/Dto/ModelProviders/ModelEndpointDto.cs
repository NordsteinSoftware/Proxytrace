using System.ComponentModel.DataAnnotations;

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
    decimal? CachedInputTokenCost,
    bool ManualPricing,
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
    decimal? InputTokenCost,
    decimal? OutputTokenCost,
    decimal? CachedInputTokenCost = null,
    bool ManualPricing = true) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Automatic mode keeps the stored prices until discovery succeeds.
        if (!ManualPricing)
            yield break;

        foreach (var (name, cost) in new[]
        {
            (nameof(InputTokenCost), InputTokenCost),
            (nameof(OutputTokenCost), OutputTokenCost),
            (nameof(CachedInputTokenCost), CachedInputTokenCost),
        })
        {
            if (cost is { } value && (value < 0 || value >= 1_000_000_000_000m || decimal.Round(value, 6) != value))
                yield return new ValidationResult(
                    "Prices must be non-negative, below 1,000,000,000,000 EUR, and have at most 6 decimal places.", [name]);
        }

        if (CachedInputTokenCost is { } cached && InputTokenCost is { } input && cached > input)
            yield return new ValidationResult("Cached-input price cannot exceed the input price.", [nameof(CachedInputTokenCost)]);
    }
}
