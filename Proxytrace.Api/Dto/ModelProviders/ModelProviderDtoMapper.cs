using Proxytrace.Api.Dto.ApiKeys;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Api.Dto.ModelProviders;

/// <summary>
/// Maps <see cref="IModelProvider"/>, <see cref="IModelEndpoint"/>, and <see cref="IApiKey"/>
/// domain entities to their DTOs for the providers controller and aggregate views.
/// </summary>
public sealed class ModelProviderDtoMapper
{
    public ModelProviderDto ToDto(IModelProvider p) =>
        new(p.Id, p.Name, p.Endpoint.ToString(), p.ApiKey, p.Kind, p.CreatedAt, p.UpdatedAt);

    /// <summary>
    /// Maps a provider without its upstream API key, for endpoints readable by non-admin
    /// members (e.g. the by-id lookup used by Tracey tools). The secret must only ever be
    /// returned from admin-gated endpoints.
    /// </summary>
    public ModelProviderDto ToRedactedDto(IModelProvider p) =>
        new(p.Id, p.Name, p.Endpoint.ToString(), string.Empty, p.Kind, p.CreatedAt, p.UpdatedAt);

    public ApiKeyDto ToKeyDto(IApiKey k)
        => ToKeyDto(k, plaintextKey: null);

    /// <summary>
    /// Maps a freshly minted key, including its plaintext value exactly once. The key is hashed at
    /// rest and unrecoverable afterwards, so this is the only chance to surface it to the caller.
    /// </summary>
    public ApiKeyDto ToCreatedKeyDto(IApiKey k, string plaintextKey)
        => ToKeyDto(k, plaintextKey);

    private static ApiKeyDto ToKeyDto(IApiKey k, string? plaintextKey)
    {
        // Enumerate the enum rather than a hand-written list: the hardcoded list silently omitted
        // ApiRead/ApiWrite, so a REST-capable key rendered in the Providers UI as having no REST
        // capability at all. Deriving from the enum means a newly added scope cannot be forgotten.
        var scopes = Enum.GetValues<ApiKeyScopes>()
            .Where(s => s != ApiKeyScopes.None && k.Scopes.HasFlag(s))
            .ToArray();
        return new(k.Id, k.Name, k.KeyPrefix, k.Project.Id, k.Project.Name, k.Provider.Id, k.Provider.Name, scopes, k.Owner.Id, k.Owner.Email, k.CreatedAt, plaintextKey);
    }

    public ModelEndpointDto ToEndpointDto(IModelEndpoint e) =>
        new(e.Id, e.Model.Name, e.Provider.Id, e.Provider.Name, e.InputTokenCost, e.OutputTokenCost, e.CachedInputTokenCost, e.CreatedAt, e.UpdatedAt);
}
