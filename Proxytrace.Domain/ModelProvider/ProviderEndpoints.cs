namespace Proxytrace.Domain.ModelProvider;

/// <summary>Runtime classification of a provider by its endpoint, without changing the entity.</summary>
public static class ProviderEndpoints
{
    /// <summary>
    /// API version used to list an Azure OpenAI resource's deployments. Azure exposes usable models
    /// as deployments here rather than through an OpenAI-style <c>/models</c> route.
    /// </summary>
    public const string AzureDeploymentsApiVersion = "2023-03-15-preview";

    /// <summary>
    /// True when the endpoint host indicates an Azure OpenAI resource. Matched as a domain suffix,
    /// not a substring: a substring test also accepts lookalike hosts such as
    /// <c>my-azure.com.example.net</c>, and misclassifying one as Azure makes the proxy attach the
    /// provider credential in a second <c>api-key</c> header on top of the bearer. A single trailing
    /// DNS root dot is normalized away first — <see cref="Uri.Host"/> (like <see cref="Uri.IdnHost"/>
    /// and <see cref="Uri.DnsSafeHost"/>) preserves it, so the fully-qualified
    /// <c>resource.openai.azure.com.</c> would otherwise fail the suffix match. Exactly one dot is
    /// trimmed: <c>resource.azure.com..</c> is not a legal hostname and stays unmatched.
    /// </summary>
    public static bool IsAzure(Uri endpoint)
    {
        string host = endpoint.Host;
        if (host.EndsWith('.'))
            host = host[..^1];

        return host.Equals(AzureDomain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{AzureDomain}", StringComparison.OrdinalIgnoreCase);
    }

    private const string AzureDomain = "azure.com";

    /// <summary>
    /// The deployments-listing URI for an Azure OpenAI resource (<c>/openai/deployments?api-version=…</c>),
    /// derived from the configured provider endpoint regardless of any <c>/openai</c> or <c>/openai/v1</c>
    /// suffix it carries.
    /// </summary>
    public static Uri AzureDeploymentsUri(Uri endpoint)
    {
        string basePath = StripOpenAiSuffix(endpoint.AbsolutePath);
        return new UriBuilder(endpoint)
        {
            Path = $"{basePath}/openai/deployments",
            Query = $"api-version={AzureDeploymentsApiVersion}",
        }.Uri;
    }

    private static string StripOpenAiSuffix(string absolutePath)
    {
        string path = absolutePath.TrimEnd('/');
        if (path.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/openai/v1".Length];
        else if (path.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/openai".Length];
        return path;
    }
}
