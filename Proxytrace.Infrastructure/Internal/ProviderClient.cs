using Nordstein.Core.AI.Clients;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using OpenAI;
using OpenAI.Models;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Infrastructure.Internal;

internal sealed class ProviderClient : IProviderClient
{
    private readonly IModelProvider provider;
    private readonly IModelRepository modelRepository;
    private readonly HttpClient http;
    private readonly IPricingService pricingService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderClient"/> class.
    /// </summary>
    public ProviderClient(
        IModelProvider provider,
        IModelRepository modelRepository,
        HttpClient http,
        IPricingService pricingService)
    {
        this.provider = provider;
        this.modelRepository = modelRepository;
        this.http = http;
        this.pricingService = pricingService;
    }

    /// <summary>
    /// Verifies the connection asynchronously.
    /// </summary>
    public async Task<ProviderConnectionResult> VerifyConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureSupportedKind();
            IReadOnlyList<DiscoveredModel> discovered = await DiscoverClassifiedAsync(cancellationToken);
            return new ProviderConnectionResult(true, null, discovered.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            return new ProviderConnectionResult(false, ProviderConnectionError.UnsupportedKind, 0);
        }
        catch (ProviderConnectionException ex)
        {
            return new ProviderConnectionResult(false, ex.Error, 0);
        }
        catch (Exception)
        {
            return new ProviderConnectionResult(false, ProviderConnectionError.Unknown, 0);
        }
    }

    /// <summary>
    /// Gets the models asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<PricedModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupportedKind();

        IReadOnlyList<DiscoveredModel> discovered = await DiscoverClassifiedAsync(cancellationToken);

        var result = new List<PricedModel>(discovered.Count);
        foreach (DiscoveredModel dm in discovered)
        {
            ModelPrice price = await pricingService.ResolveAsync(provider, dm, cancellationToken);
            IModel model = await modelRepository.GetOrCreateAsync(dm.Name, cancellationToken);
            result.Add(new PricedModel(model, price));
        }
        return result;
    }

    private async Task<IReadOnlyList<DiscoveredModel>> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (ProviderEndpoints.IsAzure(provider.Endpoint))
        {
            // Azure: deployed models only; never fall back to the (far too large) /models list.
            return await GetAzureDeploymentsAsync(cancellationToken);
        }

        IReadOnlyList<string> names = await GetOpenAiModelNamesAsync(cancellationToken);
        return names.Select(n => new DiscoveredModel(n, n)).ToArray();
    }

    private async Task<IReadOnlyList<DiscoveredModel>> DiscoverClassifiedAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await DiscoverAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new ProviderConnectionException(Classify(ex), ex);
        }
    }

    private async Task<IReadOnlyList<DiscoveredModel>> GetAzureDeploymentsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProviderEndpoints.AzureDeploymentsUri(provider.Endpoint));
        request.Headers.Add("api-key", provider.ApiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var models = new List<DiscoveredModel>();
        foreach (JsonElement item in data.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            string baseModel = item.TryGetProperty("model", out JsonElement mEl) && mEl.ValueKind == JsonValueKind.String
                ? mEl.GetString() ?? id : id;
            models.Add(new DiscoveredModel(id, baseModel));
        }
        return models;
    }

    private async Task<IReadOnlyList<string>> GetOpenAiModelNamesAsync(CancellationToken cancellationToken)
    {
        OpenAIModelClient modelClient = CreateOpenAiClient();
        var result = (await modelClient.GetModelsAsync(cancellationToken)).Value.ToArray();
        return result.Select(m => m.Id).ToArray();
    }

    private OpenAIModelClient CreateOpenAiClient()
    {
        EnsureSupportedKind();
        var credential = new ApiKeyCredential(provider.ApiKey);
        var options = new OpenAIClientOptions
        {
            Endpoint = provider.Endpoint,
            Transport = new HttpClientPipelineTransport(http),
        };
        return new OpenAIModelClient(credential, options);
    }

    private static ProviderConnectionError Classify(Exception exception)
    {
        if (exception is ClientResultException { Status: 401 or 403 })
            return ProviderConnectionError.Unauthorized;

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            return ProviderConnectionError.Unauthorized;

        if (exception is HttpRequestException or TimeoutException or OperationCanceledException
            || exception is ClientResultException { Status: 0 or 408 })
            return ProviderConnectionError.NetworkError;

        if (exception is NotSupportedException)
            return ProviderConnectionError.UnsupportedKind;

        if (exception is AggregateException aggregate)
        {
            ProviderConnectionError[] errors = aggregate.Flatten().InnerExceptions.Select(Classify).ToArray();
            if (errors.Contains(ProviderConnectionError.Unauthorized))
                return ProviderConnectionError.Unauthorized;
            if (errors.Length > 0 && errors.All(error => error == ProviderConnectionError.NetworkError))
                return ProviderConnectionError.NetworkError;
        }

        return ProviderConnectionError.Unknown;
    }

    private void EnsureSupportedKind()
    {
        if (provider.Kind is not ModelProviderKind.OpenAi and not ModelProviderKind.OpenAiCompatible)
            throw new NotSupportedException($"Model provider kind {provider.Kind} is not supported");
    }
}
