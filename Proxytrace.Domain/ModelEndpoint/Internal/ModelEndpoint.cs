using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.ModelEndpoint.Internal;

internal record ModelEndpoint : DomainEntity<IModelEndpoint>, IModelEndpoint
{
    public IModel Model { get; }
    public IModelProvider Provider { get; }
    public decimal? InputTokenCost { get; }
    public decimal? OutputTokenCost { get; }
    public decimal? CachedInputTokenCost { get; }

    public ModelEndpoint(
        IModel model,
        IModelProvider provider,
        decimal? inputTokenCost,
        decimal? outputTokenCost,
        decimal? cachedInputTokenCost,
        IRepository<IModelEndpoint> repository) : base(repository)
    {
        Model = model;
        Provider = provider;
        InputTokenCost = inputTokenCost;
        OutputTokenCost = outputTokenCost;
        CachedInputTokenCost = cachedInputTokenCost;
    }

    public ModelEndpoint(
        IModel model,
        IModelProvider provider,
        decimal? inputTokenCost,
        decimal? outputTokenCost,
        decimal? cachedInputTokenCost,
        IDomainEntityData existing,
        IRepository<IModelEndpoint> repository)
        : base(existing, repository)
    {
        Model = model;
        Provider = provider;
        InputTokenCost = inputTokenCost;
        OutputTokenCost = outputTokenCost;
        CachedInputTokenCost = cachedInputTokenCost;
    }

    /// <summary>
    /// Computes the EUR cost of a usage. The cached input subset is priced at
    /// <see cref="CachedInputTokenCost"/> (falling back to <see cref="InputTokenCost"/> when no
    /// cached price is configured); the remaining input at <see cref="InputTokenCost"/>. Returns
    /// <c>null</c> when either input or output price is unknown.
    /// </summary>
    public decimal? CalculateCost(TokenUsage usage)
    {
        if (InputTokenCost is not { } inputCost || OutputTokenCost is not { } outputCost)
            return null;

        ulong cached = Math.Min(usage.CachedInputTokenCount, usage.InputTokenCount);
        ulong uncachedInput = usage.InputTokenCount - cached;
        decimal cachedCost = CachedInputTokenCost ?? inputCost;

        return (inputCost * uncachedInput + cachedCost * cached + outputCost * usage.OutputTokenCount) /
               1_000_000m;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Model.Validate(validationContext))
            yield return result;

        foreach (var result in Provider.Validate(validationContext))
            yield return result;

        // Zero is a valid, known price (free/local models — e.g. a self-hosted endpoint or a
        // provider's free tier); null means "unknown", which makes CalculateCost return null
        // instead of 0. Only negative prices are invalid.
        if (InputTokenCost.HasValue)
            yield return Validation.NotNegative(InputTokenCost.Value, nameof(InputTokenCost));

        if (OutputTokenCost.HasValue)
            yield return Validation.NotNegative(OutputTokenCost.Value, nameof(OutputTokenCost));

        if (CachedInputTokenCost.HasValue)
            yield return Validation.NotNegative(CachedInputTokenCost.Value, nameof(CachedInputTokenCost));

        // No InputTokenCost <= OutputTokenCost invariant: output is usually >= input but not
        // universally (some batch/cached/reasoning tiers price input >= output), and CalculateCost
        // does not rely on the ordering.
        if (CachedInputTokenCost.HasValue && InputTokenCost.HasValue)
            yield return Validation.LessThanOrEqual(CachedInputTokenCost.Value, InputTokenCost.Value, nameof(CachedInputTokenCost));
    }
}
