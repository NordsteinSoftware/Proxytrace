using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Dto.Setup;
using Proxytrace.Application.Cleanup;
using Proxytrace.Application.ErrorLog;
using Proxytrace.Application.Setup;
using Nordstein.Core.Common.Net;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for setup operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private readonly IRepository<IUser> userRepository;
    private readonly IRepository<IProject> projectRepository;
    private readonly IDataCleanupService cleanup;
    private readonly ISetupService setup;
    private readonly ILogger<Audit> audit;
    private readonly ILogger<SetupController> logger;
    private readonly IWebHostEnvironment env;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupController"/> class.
    /// </summary>
    public SetupController(
        IRepository<IUser> userRepository,
        IRepository<IProject> projectRepository,
        IDataCleanupService cleanup,
        ISetupService setup,
        ILogger<Audit> audit,
        ILogger<SetupController> logger,
        IWebHostEnvironment env)
    {
        this.userRepository = userRepository;
        this.projectRepository = projectRepository;
        this.cleanup = cleanup;
        this.setup = setup;
        this.audit = audit;
        this.logger = logger;
        this.env = env;
    }

    /// <summary>
    /// Gets the status.
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<SetupStatusDto> GetStatus(CancellationToken cancellationToken)
    {
        var users = await this.userRepository.CountAsync(cancellationToken);
        var projects = await this.projectRepository.CountAsync(cancellationToken);
        return new SetupStatusDto { IsConfigured = users > 0 && projects > 0 };
    }

    /// <summary>
    /// Completes.
    /// </summary>
    [HttpPost("complete")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CompleteSetupResponse>> Complete(
        [FromBody] CompleteSetupRequest request,
        CancellationToken cancellationToken)
    {
        if (await projectRepository.CountAsync(cancellationToken) > 0)
            return Conflict("Setup has already been completed.");

        var input = new SetupInput(
            request.ProviderName,
            request.ProviderEndpoint.ToEndpointUri(),
            request.ProviderUpstreamApiKey,
            request.ProviderKind,
            request.ModelName,
            request.ProjectName);

        var result = await setup.CompleteAsync(input, cancellationToken);

        // The setup wizard creates exactly the entities every other path audits — including a model
        // provider carrying an upstream credential — so it must leave the same record. Without these
        // the compliance log has no trace of who configured the instance's first provider key.
        audit.LogAudit(AuditAction.ProviderConfigCreated, nameof(IModelProvider), result.ProviderId, request.ProviderName);
        audit.LogAudit(AuditAction.EndpointConfigCreated, nameof(IModelEndpoint), result.EndpointId, request.ModelName);
        audit.LogAudit(AuditAction.ProjectCreated, nameof(IProject), result.ProjectId, request.ProjectName, projectId: result.ProjectId);

        return new CompleteSetupResponse(
            result.ProviderId,
            result.EndpointId,
            result.ProjectId);
    }

    /// <summary>
    /// Test connection.
    /// </summary>
    [HttpPost("test-connection")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<TestConnectionResponse> TestConnection(
        [FromBody] TestConnectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = new ProviderConnectionInput(
                request.ProviderName,
                request.ProviderEndpoint.ToEndpointUri(),
                request.ProviderUpstreamApiKey,
                request.ProviderKind);
            ProviderConnectionResult result = await setup.TestProviderConnectionAsync(input, cancellationToken);
            return new TestConnectionResponse(
                result.Success,
                result.Error?.ToString(),
                result.ModelCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Mirror ExceptionHandlingMiddleware: capture under an error id and only surface the raw
            // message in Development. Outside Development it may carry provider endpoint/credential or
            // other internal detail, so return a generic message carrying the error id for support.
            var errorId = Guid.NewGuid();
            using (logger.BeginScope(new Dictionary<string, object> { [ErrorLogScope.ErrorIdKey] = errorId }))
            {
                logger.LogError(ex, "Provider connection test failed");
            }

            string? message = env.IsDevelopment() ? ex.Message : null;
            return new TestConnectionResponse(
                false,
                ProviderConnectionError.Unknown.ToString(),
                0,
                message,
                errorId);
        }
    }

    /// <summary>
    /// Lists the models.
    /// </summary>
    [HttpPost("list-models")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ListModelsResponse> ListModels(
        [FromBody] ListModelsRequest request,
        CancellationToken cancellationToken)
    {
        var input = new ProviderConnectionInput(
            request.ProviderName,
            request.ProviderEndpoint.ToEndpointUri(),
            request.ProviderUpstreamApiKey,
            request.ProviderKind);
        var models = await setup.ListProviderModelsAsync(input, cancellationToken);
        return new ListModelsResponse(models);
    }

    /// <summary>
    /// Cleanup non model data.
    /// </summary>
    [HttpPost("cleanup")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> CleanupNonModelData(CancellationToken cancellationToken)
    {
        await cleanup.DeleteAllNonModelDataAsync(cancellationToken);
        audit.LogAudit(AuditAction.SetupCleanupPurged, "Setup");
        return NoContent();
    }
}
