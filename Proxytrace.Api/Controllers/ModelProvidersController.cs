using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Nordstein.Core.Common.Net;
using Nordstein.Core.Common.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Dto.ApiKeys;
using Proxytrace.Application.Pricing;
using Proxytrace.Api.Dto.ModelProviders;
using Proxytrace.Api.Dto.Projects;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Paging;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Application.Auth;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/providers")]
public class ModelProvidersController : ControllerBase
{
    private readonly IModelProviderRepository providerRepository;
    private readonly IApiKeyRepository apiKeyRepository;
    private readonly IProjectRepository projectRepository;
    private readonly IModelEndpointRepository endpointRepository;
    private readonly IModelRepository modelRepository;
    private readonly IModelProvider.CreateNew createProvider;
    private readonly IModelProvider.CreateExisting updateProvider;
    private readonly IApiKey.CreateNew createApiKey;
    private readonly IModelEndpoint.CreateNew createEndpoint;
    private readonly IModelEndpoint.CreateExisting updateEndpoint;
    private readonly ModelProviderDtoMapper mapper;
    private readonly IModelPriceRefresher priceRefresher;
    private readonly ICurrentUserAccessor currentUser;
    private readonly IRepository<IUser> users;
    private readonly ILogger<Audit> audit;

    public ModelProvidersController(
        IModelProviderRepository providerRepository,
        IApiKeyRepository apiKeyRepository,
        IProjectRepository projectRepository,
        IModelEndpointRepository endpointRepository,
        IModelProvider.CreateNew createProvider,
        IModelProvider.CreateExisting updateProvider,
        IApiKey.CreateNew createApiKey,
        IModelRepository modelRepository,
        IModelEndpoint.CreateNew createEndpoint,
        IModelEndpoint.CreateExisting updateEndpoint,
        ModelProviderDtoMapper mapper,
        IModelPriceRefresher priceRefresher,
        ICurrentUserAccessor currentUser,
        IRepository<IUser> users,
        ILogger<Audit> audit)
    {
        this.providerRepository = providerRepository;
        this.apiKeyRepository = apiKeyRepository;
        this.projectRepository = projectRepository;
        this.endpointRepository = endpointRepository;
        this.modelRepository = modelRepository;
        this.audit = audit;
        this.createProvider = createProvider;
        this.updateProvider = updateProvider;
        this.createApiKey = createApiKey;
        this.createEndpoint = createEndpoint;
        this.updateEndpoint = updateEndpoint;
        this.mapper = mapper;
        this.priceRefresher = priceRefresher;
        this.currentUser = currentUser;
        this.users = users;
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<PagedResult<ModelProviderDto>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var paged = await providerRepository.GetPagedAsync(page, pageSize, cancellationToken);
        return paged.Map(mapper.ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ModelProviderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var provider = await providerRepository.FindAsync(id, cancellationToken);
        if (provider is null)
            return NotFound();
        // Readable by every authenticated member (Tracey tools), so the upstream key is redacted;
        // admin views read the key from the admin-gated overview/list endpoints instead.
        return mapper.ToRedactedDto(provider);
    }

    /// <summary>
    /// Returns a provider's upstream credential in the clear.
    /// </summary>
    /// <remarks>
    /// The key used to be projected into the ordinary list/overview responses, so every provider
    /// credential reached the browser on every admin providers-page load, whether or not anyone
    /// pressed "reveal" — and nothing recorded who had seen what. It now has to be asked for
    /// explicitly, and every read is audited.
    /// </remarks>
    [HttpGet("{id:guid}/key")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ModelProviderKeyDto>> GetUpstreamKey(Guid id, CancellationToken cancellationToken)
    {
        var provider = await providerRepository.FindAsync(id, cancellationToken);
        if (provider is null)
            return NotFound();

        // Audit before returning, so a read is recorded even if the response never reaches the
        // client. Only the fact of the read is recorded — never the key value.
        audit.LogAudit(AuditAction.ProviderUpstreamKeyRevealed, nameof(IModelProvider), provider.Id, provider.Name);
        return new ModelProviderKeyDto(provider.ApiKey);
    }

    [HttpGet("overview")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ProvidersOverviewDto> GetOverview(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<IModelProvider>> providersTask = providerRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<IModelEndpoint>> endpointsTask = endpointRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<IApiKey>> keysTask = apiKeyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<IProject>> projectsTask = projectRepository.GetAllAsync(cancellationToken);

        await Task.WhenAll(providersTask, endpointsTask, keysTask, projectsTask);

        ILookup<Guid, IModelEndpoint> endpointsByProvider = endpointsTask.Result.ToLookup(e => e.Provider.Id);
        ILookup<Guid, IApiKey> keysByProvider = keysTask.Result.ToLookup(k => k.Provider.Id);

        var providers = providersTask.Result
            .Select(p => new ProviderWithDetailsDto(
                mapper.ToDto(p),
                endpointsByProvider[p.Id].Select(mapper.ToEndpointDto).ToArray(),
                keysByProvider[p.Id].Select(mapper.ToKeyDto).ToArray()))
            .ToArray();

        return new ProvidersOverviewDto(
            providers,
            projectsTask.Result.Select(ProjectDtoMapper.ToDto).ToArray());
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ModelProviderDto>> Create(
        [FromBody] CreateModelProviderRequest request,
        CancellationToken cancellationToken)
    {
        var provider = createProvider(request.Name, request.Endpoint.ToEndpointUri(), request.UpstreamApiKey, request.Kind);
        var saved = await providerRepository.AddAsync(provider, cancellationToken);
        await priceRefresher.RefreshProviderAsync(saved, cancellationToken);
        audit.LogAudit(AuditAction.ProviderConfigCreated, nameof(IModelProvider), saved.Id, saved.Name);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, mapper.ToDto(saved));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ModelProviderDto>> Update(
        Guid id,
        [FromBody] UpdateModelProviderRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await providerRepository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();
        // A null key means "leave the credential alone" — the client no longer receives it, so an
        // edit that only renames the provider has nothing to send back.
        string apiKey = request.UpstreamApiKey ?? existing.ApiKey;

        // Credential rotation is a security-relevant action of its own; compare against the stored
        // key before it is overwritten so the audit trail distinguishes a rotation from an
        // endpoint/name/kind edit. Only the fact of the change is recorded — never the key value.
        bool keyRotated = existing.ApiKey != apiKey;
        bool configChanged = existing.Name != request.Name
            || existing.Endpoint != request.Endpoint.ToEndpointUri()
            || existing.Kind != request.Kind;
        var updated = updateProvider(request.Name, request.Endpoint.ToEndpointUri(), apiKey, request.Kind, existing);
        var saved = await providerRepository.UpdateAsync(updated, cancellationToken);
        if (keyRotated)
            audit.LogAudit(AuditAction.ProviderUpstreamKeyRotated, nameof(IModelProvider), saved.Id, saved.Name);
        if (configChanged || !keyRotated)
            audit.LogAudit(AuditAction.ProviderConfigUpdated, nameof(IModelProvider), saved.Id, saved.Name);
        return mapper.ToDto(saved);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        // Archive (soft-delete) instead of hard-deleting: a hard delete cascades through the
        // provider's endpoints to every AgentCall/TestRun that referenced them, silently destroying
        // history. Archiving hides the provider + its endpoints from listings while preserving that
        // history. Contract unchanged (204/404), so the frontend needs no change.
        var provider = await providerRepository.FindAsync(id, cancellationToken);
        if (provider is null)
            return NotFound();
        // Audit only a real state transition: ArchiveAsync returns false for an already-archived
        // provider, so a repeated delete is a 404 no-op and records no phantom deletion.
        if (!await providerRepository.ArchiveAsync(id, cancellationToken))
            return NotFound();
        audit.LogAudit(AuditAction.ProviderConfigDeleted, nameof(IModelProvider), id, provider.Name);
        return NoContent();
    }

    // ── Model Endpoints ───────────────────────────────────────────────────────

    [HttpGet("/api/model-endpoints")]
    public async Task<IReadOnlyList<ModelEndpointDto>> GetAllModelEndpoints(CancellationToken cancellationToken)
    {
        var all = await endpointRepository.GetAllAsync(cancellationToken);
        return all.Select(mapper.ToEndpointDto).ToArray();
    }

    // ── Models ────────────────────────────────────────────────────────────────

    [HttpPost("{providerId:guid}/reload")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<IReadOnlyList<ModelEndpointDto>>> Reload(
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = await providerRepository.FindAsync(providerId, cancellationToken);
        if (provider is null)
            return NotFound("Provider not found.");

        await priceRefresher.RefreshProviderAsync(provider, cancellationToken);

        var endpoints = await endpointRepository.GetByProviderAsync(providerId, cancellationToken);
        return endpoints.Select(mapper.ToEndpointDto).ToArray();
    }

    [HttpGet("{providerId:guid}/available-models")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<IReadOnlyList<string>>> GetAvailableModels(Guid providerId, CancellationToken cancellationToken)
    {
        var provider = await providerRepository.GetAsync(providerId, cancellationToken);
        try
        {
            var discovered = await provider.CreateClient().GetModelsAsync(cancellationToken);
            return discovered.Select(m => m.Model.Name).OrderBy(n => n).ToArray();
        }
        catch (ProviderConnectionException ex)
        {
            return BadRequest(ex.Error.ToString());
        }
        catch (NotSupportedException)
        {
            return BadRequest(ProviderConnectionError.UnsupportedKind.ToString());
        }
    }

    [HttpGet("{providerId:guid}/models")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<IReadOnlyList<ModelEndpointDto>>> GetModels(Guid providerId, CancellationToken cancellationToken)
    {
        if (!await providerRepository.ContainsAsync(providerId, cancellationToken))
            return NotFound("Provider not found.");
        var endpoints = await endpointRepository.GetByProviderAsync(providerId, cancellationToken);
        return endpoints.Select(mapper.ToEndpointDto).ToArray();
    }

    [HttpPost("{providerId:guid}/models")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ModelEndpointDto>> CreateModel(
        Guid providerId,
        [FromBody] CreateModelEndpointRequest request,
        CancellationToken cancellationToken)
    {
        var provider = await providerRepository.FindAsync(providerId, cancellationToken);
        if (provider is null)
            return NotFound("Provider not found.");

        var providerEndpoints = await endpointRepository.GetByProviderAsync(providerId, cancellationToken);
        if (providerEndpoints.Any(e => e.Model.Name == request.ModelName))
            return Conflict(new { error = $"A model endpoint for '{request.ModelName}' already exists for this provider." });

        IModel model = await modelRepository.GetOrCreateAsync(request.ModelName, cancellationToken);

        var endpoint = createEndpoint(model, provider, request.InputTokenCost, request.OutputTokenCost, cachedInputTokenCost: null);
        var saved = await endpointRepository.AddAsync(endpoint, cancellationToken);
        audit.LogAudit(AuditAction.EndpointConfigCreated, nameof(IModelEndpoint), saved.Id, saved.Model.Name);
        return CreatedAtAction(nameof(GetModels), new { providerId }, mapper.ToEndpointDto(saved));
    }

    [HttpDelete("endpoints/{endpointId:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteModel(Guid endpointId, CancellationToken cancellationToken)
    {
        // Soft-delete: archiving hides the endpoint from pickers but keeps the row, so agents that
        // still reference it (and their captured calls / test runs) keep resolving. See
        // ArchivableRepository.
        var endpoint = await endpointRepository.FindAsync(endpointId, cancellationToken);
        if (endpoint is null)
            return NotFound();
        // Audit only a real state transition: a repeated delete of an already-archived endpoint
        // returns false (404 no-op) and records no phantom deletion.
        if (!await endpointRepository.ArchiveAsync(endpointId, cancellationToken))
            return NotFound();
        audit.LogAudit(AuditAction.EndpointConfigDeleted, nameof(IModelEndpoint), endpointId, endpoint.Model.Name);
        return NoContent();
    }

    [HttpPut("{providerId:guid}/models/{endpointId:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ModelEndpointDto>> UpdateModelPricing(
        Guid providerId,
        Guid endpointId,
        [FromBody] UpdateModelEndpointPricingRequest request,
        CancellationToken cancellationToken)
    {
        if (!await providerRepository.ContainsAsync(providerId, cancellationToken))
            return NotFound("Provider not found.");

        var existing = await endpointRepository.FindAsync(endpointId, cancellationToken);
        if (existing is null)
            return NotFound("Model endpoint not found.");

        if (existing.Provider.Id != providerId)
            return NotFound("Model endpoint not found.");

        // Cached-input price is auto-fetched only (not user-editable) — preserve the existing value
        // so a manual input/output edit never wipes it.
        var updated = updateEndpoint(
            existing.Model, existing.Provider, request.InputTokenCost, request.OutputTokenCost,
            existing.CachedInputTokenCost, existing);
        var saved = await endpointRepository.UpdateAsync(updated, cancellationToken);
        audit.LogAudit(AuditAction.EndpointConfigUpdated, nameof(IModelEndpoint), saved.Id, saved.Model.Name);
        return mapper.ToEndpointDto(saved);
    }

    // ── API Keys ──────────────────────────────────────────────────────────────

    [HttpGet("{providerId:guid}/keys")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IReadOnlyList<ApiKeyDto>> GetKeys(Guid providerId, CancellationToken cancellationToken)
    {
        var keys = await apiKeyRepository.GetByProviderAsync(providerId, cancellationToken);
        return keys.Select(mapper.ToKeyDto).ToArray();
    }

    [HttpPost("{providerId:guid}/keys")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ApiKeyDto>> CreateKey(
        Guid providerId,
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var provider = await providerRepository.FindAsync(providerId, cancellationToken);
        if (provider is null)
            return NotFound("Provider not found.");
        var project = await projectRepository.FindAsync(request.ProjectId, cancellationToken);
        if (project is null)
            return BadRequest($"Project {request.ProjectId} not found.");

        // The key acts as a user; every MCP call made with it is attributed to that owner. An explicit
        // userId assigns the key to that user, otherwise it is owned by the admin creating it.
        IUser? owner = request.UserId.HasValue
            ? await users.FindAsync(request.UserId.Value, cancellationToken)
            : await currentUser.GetCurrentUserAsync(cancellationToken);
        if (owner is null)
            return BadRequest(request.UserId.HasValue
                ? $"User {request.UserId} not found."
                : "No authenticated user is available to own the key.");

        // The proxy bearer credential must be unguessable, so derive it from a CSPRNG (256 bits,
        // url-safe base64) rather than Guid.NewGuid, which carries no cryptographic-strength contract.
        Span<byte> keyBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var keyValue = $"proxytrace-{Convert.ToBase64String(keyBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

        // The key is verify-only, so only its hash is persisted; a short prefix is kept so it can be
        // recognised in lists. The plaintext is returned once below and is unrecoverable afterwards.
        var keyHash = Sha256.HexHash(keyValue);
        var keyPrefix = keyValue.Length <= 16 ? keyValue : keyValue[..16];

        // Least privilege: a key grants only the scopes explicitly requested; an unspecified set falls
        // back to Ingestion-only so a key is never silently MCP-capable.
        var scopes = request.Scopes is { Count: > 0 }
            ? request.Scopes.Aggregate(ApiKeyScopes.None, (acc, s) => acc | s)
            : ApiKeyScopes.Ingestion;
        var key = createApiKey(request.Name, keyHash, keyPrefix, project, provider, scopes, owner);
        var saved = await apiKeyRepository.AddAsync(key, cancellationToken);
        audit.LogAudit(
            AuditAction.ApiKeyMinted,
            nameof(IApiKey),
            saved.Id,
            saved.Name,
            projectId: project.Id,
            details: JsonSerializer.Serialize(new { scopes = scopes.ToString(), ownerEmail = owner.Email }));
        return CreatedAtAction(nameof(GetKeys), new { providerId }, mapper.ToCreatedKeyDto(saved, keyValue));
    }

    [HttpDelete("{providerId:guid}/keys/{keyId:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteKey(Guid providerId, Guid keyId, CancellationToken cancellationToken)
    {
        var key = await apiKeyRepository.FindAsync(keyId, cancellationToken);
        if (key is null || key.Provider.Id != providerId)
            return NotFound();
        var removed = await apiKeyRepository.RemoveAsync(keyId, cancellationToken);
        if (!removed)
            return NotFound();

        audit.LogAudit(AuditAction.ApiKeyDeleted, nameof(IApiKey), keyId, key.Name, projectId: key.Project.Id);
        return NoContent();
    }

}
