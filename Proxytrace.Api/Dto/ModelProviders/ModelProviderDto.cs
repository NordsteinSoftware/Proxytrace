using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Api.Dto.ModelProviders;

/// <summary>
/// A configured upstream provider.
/// </summary>
/// <param name="UpstreamApiKeyPreview">
/// A masked rendering of the upstream credential — enough to tell two keys apart in the UI, never
/// enough to use. The key itself is returned only by <c>GET /api/providers/{id}/key</c>, which is
/// admin-gated and audited; it used to ship with every providers-page load, so no record existed of
/// who had actually seen it.
/// </param>
public record ModelProviderDto(
    Guid Id,
    string Name,
    string Endpoint,
    string UpstreamApiKeyPreview,
    ModelProviderKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The cleartext upstream credential, returned only from the audited reveal endpoint.</summary>
public record ModelProviderKeyDto(string UpstreamApiKey);

/// <summary>
/// Request payload for create model provider operations.
/// </summary>
public record CreateModelProviderRequest(string Name, string Endpoint, string UpstreamApiKey, ModelProviderKind Kind);

/// <param name="UpstreamApiKey">
/// The replacement credential, or <see langword="null"/> to leave the stored one untouched. Nullable
/// because the client no longer holds the key: it is not part of <see cref="ModelProviderDto"/>, so
/// an edit that only renames a provider or changes its kind has nothing to echo back — and sending
/// an empty string would wipe the credential.
/// </param>
public record UpdateModelProviderRequest(string Name, string Endpoint, string? UpstreamApiKey, ModelProviderKind Kind);
