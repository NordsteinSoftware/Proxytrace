using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;

namespace Proxytrace.Proxy;

/// <summary>
/// Resolves a raw inbound bearer token (and optional project slug from the request path) to a
/// <see cref="ResolvedApiKey"/> on the proxy hot path. Accepts either a Proxytrace-issued
/// <see cref="Domain.ApiKey.IApiKey"/> (which carries its own project) or the provider's own
/// upstream <see cref="Domain.ModelProvider.IModelProvider.ApiKey"/> (which needs the project
/// slug from the path for attribution); the Proxytrace key wins if the same string matches both.
/// Resolution is deliberately uncached: it hits storage on every request so key rotation and
/// revocation take effect immediately, and it fails closed (the request errors) when the database
/// is unreachable rather than serving stale credentials (#407).
/// </summary>
public interface IApiKeyResolver
{
    /// <summary>
    /// Resolves the inbound credentials. <paramref name="projectSlug"/> is the project segment from
    /// the request path (e.g. <c>/{project}/openai/v1/…</c>), or <see langword="null"/> when the
    /// caller used the legacy <c>/openai/v1/…</c> form. It is required for the upstream-key path and,
    /// when supplied alongside a Proxytrace key, must match that key's project. Returns
    /// <see langword="null"/> when authentication or attribution fails.
    /// </summary>
    Task<ResolvedApiKey?> ResolveAsync(string rawKey, string? projectSlug, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of resolving an inbound bearer token on the proxy hot path. Carries the upstream
/// provider to forward to and the project to attribute the captured call to, independent of
/// which authentication path matched (a Proxytrace-issued <see cref="Domain.ApiKey.IApiKey"/>
/// or the provider's own upstream <see cref="IModelProvider.ApiKey"/>).
/// </summary>
/// <param name="Project">The project to attribute the captured call to.</param>
/// <param name="Provider">The upstream provider to forward the request to.</param>
/// <param name="ApiKeyId">
/// The id of the Proxytrace-issued key that authenticated the request, or <see langword="null"/> on
/// the upstream-key path, where no such key exists. Carried through to the trace so spend can be
/// attributed per key, and used by the proxy's key-scoped budget block — which is therefore inert
/// for upstream-key traffic.
/// </param>
/// <param name="Scopes">
/// The capabilities of the Proxytrace-issued key that authenticated the call, or
/// <see langword="null"/> when the caller authenticated with the provider's <b>own</b> upstream
/// credential.
/// </param>
/// <remarks>
/// A null <paramref name="Scopes"/> means "unscoped, and legitimately so": a caller holding the
/// provider's own key can already call the provider directly, so restricting which of the
/// provider's paths they may reach through Proxytrace would protect nothing. <paramref name="ApiKeyId"/>
/// is null on that same path, for the same reason — there is no Proxytrace-issued key to attribute to.
/// </remarks>
public sealed record ResolvedApiKey(
    IProject Project,
    IModelProvider Provider,
    Guid? ApiKeyId = null,
    ApiKeyScopes? Scopes = null);
