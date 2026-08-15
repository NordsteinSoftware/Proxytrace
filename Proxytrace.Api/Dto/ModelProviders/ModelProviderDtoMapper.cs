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
    /// <summary>
    /// Maps a provider, masking its upstream credential.
    /// </summary>
    /// <remarks>
    /// This mapper has <b>no</b> variant that emits the cleartext key: it used to, and the result
    /// was that every provider credential in the installation was sent to the browser on every
    /// admin providers-page load — whether or not anyone pressed "reveal" — with nothing recording
    /// who saw what. The key is now served only by the dedicated, audited reveal endpoint. Keep it
    /// that way; do not add a "full" overload here.
    /// </remarks>
    public ModelProviderDto ToDto(IModelProvider p) =>
        new(p.Id, p.Name, p.Endpoint.ToString(), Mask(p.ApiKey), p.Kind, p.CreatedAt, p.UpdatedAt);

    /// <summary>
    /// Maps a provider without even the masked credential, for endpoints readable by non-admin
    /// members (e.g. the by-id lookup used by Tracey tools).
    /// </summary>
    public ModelProviderDto ToRedactedDto(IModelProvider p) =>
        new(p.Id, p.Name, p.Endpoint.ToString(), string.Empty, p.Kind, p.CreatedAt, p.UpdatedAt);

    /// <summary>
    /// Renders a credential as a preview: enough to tell two keys apart and to confirm a rotation
    /// took effect, never enough to use. Short keys are masked whole rather than leaking a
    /// proportionally large share of themselves — self-hosted backends conventionally use values
    /// like <c>EMPTY</c> or <c>ollama</c>.
    /// </summary>
    private static string Mask(string key)
        => key.Length switch
        {
            0 => string.Empty,
            <= 8 => new string('•', 8),
            _ => key[..3] + new string('•', 8) + key[^4..],
        };

    /// <summary>
    /// To key dto.
    /// </summary>
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

    /// <summary>
    /// To endpoint dto.
    /// </summary>
    public ModelEndpointDto ToEndpointDto(IModelEndpoint e) =>
        new(e.Id, e.Model.Name, e.Provider.Id, e.Provider.Name, e.InputTokenCost, e.OutputTokenCost, e.CachedInputTokenCost, e.CreatedAt, e.UpdatedAt);
}
