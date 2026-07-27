using System.Text;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Domain.Kiosk;

/// <summary>
/// Optional real LLM endpoint for kiosk mode, bound from the <c>Kiosk:Endpoint</c>
/// configuration section. When configured, kiosk seeding creates a real model provider,
/// model and endpoint and uses it as the project's system endpoint and for the demo agents,
/// turning the kiosk into a fully functional demo (Tracey chat and test runs hit a real LLM).
/// </summary>
public sealed record KioskEndpointOptions
{
    /// <summary>
    /// The provider's API base URL (e.g. <c>https://api.openai.com/v1</c>).
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The provider API key used to authenticate upstream LLM calls.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// The model name to register (e.g. <c>gpt-4o</c>).
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// The provider kind, parsed to <see cref="ModelProviderKind"/> (e.g. <c>OpenAi</c>, <c>OpenAiCompatible</c>).
    /// </summary>
    public string Kind { get; init; } = nameof(ModelProviderKind.OpenAi);

    /// <summary>
    /// Display name for the seeded provider.
    /// </summary>
    public string ProviderName { get; init; } = "Kiosk Provider";

    /// <summary>
    /// Optional price of 1M input tokens (EUR).
    /// </summary>
    public decimal? InputTokenCost { get; init; }

    /// <summary>
    /// Optional price of 1M output tokens (EUR).
    /// </summary>
    public decimal? OutputTokenCost { get; init; }

    /// <summary>
    /// Whether any of the three credential fields (<see cref="BaseUrl"/>, <see cref="ApiKey"/>,
    /// <see cref="Model"/>) is set. Distinguishes an all-blank section — which the composition root treats
    /// as ABSENT (the read-only kiosk) — from a partially filled one that <see cref="Resolve"/> rejects.
    /// The <see cref="Kind"/>/<see cref="ProviderName"/> defaults are deliberately ignored: they are
    /// non-null by design and would otherwise make an empty section look configured.
    /// </summary>
    public bool HasAnyCredential =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        || !string.IsNullOrWhiteSpace(ApiKey)
        || !string.IsNullOrWhiteSpace(Model);

    /// <summary>
    /// Whether all required fields (<see cref="BaseUrl"/>, <see cref="ApiKey"/>, <see cref="Model"/>)
    /// are present.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model);

    /// <summary>
    /// Validates and resolves the options into a strongly-typed endpoint descriptor.
    /// Throws when the section is present but incomplete or invalid.
    /// </summary>
    public ResolvedKioskEndpoint Resolve()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)
            || string.IsNullOrWhiteSpace(ApiKey)
            || string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException(
                "Kiosk:Endpoint is partially configured. BaseUrl, ApiKey and Model are all required.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Kiosk:Endpoint:BaseUrl is not a valid absolute URL: '{BaseUrl}'.");
        }

        if (!Enum.TryParse<ModelProviderKind>(Kind, ignoreCase: true, out var kind)
            || kind == ModelProviderKind.Unknown)
        {
            throw new InvalidOperationException(
                $"Kiosk:Endpoint:Kind '{Kind}' is not a valid provider kind "
                + $"({nameof(ModelProviderKind.OpenAi)}, "
                + $"{nameof(ModelProviderKind.OpenAiCompatible)}).");
        }

        return new ResolvedKioskEndpoint(
            baseUri,
            ApiKey,
            Model,
            kind,
            ProviderName,
            InputTokenCost,
            OutputTokenCost);
    }

    // Redact the upstream provider credential from the record's generated ToString()/PrintMembers so
    // it never leaks into a log line, exception message or debugger string — the same treatment
    // ModelProvider gives its API key. Note Resolve() already throws with the *unredacted* BaseUrl
    // and Kind only, never the key. Private, not protected virtual, because this record is sealed and
    // derives from object.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("BaseUrl = ").Append(BaseUrl)
            .Append(", ApiKey = ***")
            .Append(", Model = ").Append(Model)
            .Append(", Kind = ").Append(Kind)
            .Append(", ProviderName = ").Append(ProviderName)
            .Append(", InputTokenCost = ").Append(InputTokenCost)
            .Append(", OutputTokenCost = ").Append(OutputTokenCost)
            .Append(", HasAnyCredential = ").Append(HasAnyCredential)
            .Append(", IsConfigured = ").Append(IsConfigured);
        return true;
    }
}

/// <summary>
/// A validated, non-null kiosk endpoint descriptor produced by <see cref="KioskEndpointOptions.Resolve"/>.
/// </summary>
public sealed record ResolvedKioskEndpoint(
    Uri BaseUrl,
    string ApiKey,
    string Model,
    ModelProviderKind Kind,
    string ProviderName,
    decimal? InputTokenCost,
    decimal? OutputTokenCost)
{
    // Same redaction as KioskEndpointOptions: this record carries the resolved, definitely-present
    // upstream credential, so its textual rendering must never contain it.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("BaseUrl = ").Append(BaseUrl)
            .Append(", ApiKey = ***")
            .Append(", Model = ").Append(Model)
            .Append(", Kind = ").Append(Kind)
            .Append(", ProviderName = ").Append(ProviderName)
            .Append(", InputTokenCost = ").Append(InputTokenCost)
            .Append(", OutputTokenCost = ").Append(OutputTokenCost);
        return true;
    }
}
