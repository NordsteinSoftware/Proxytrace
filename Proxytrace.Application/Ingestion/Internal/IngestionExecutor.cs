using System.Net;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;

namespace Proxytrace.Application.Ingestion.Internal;

/// <summary>
/// In-process ingestion: the shared core used both by the <see cref="AgentCallIngestionWorker"/>
/// (per stream envelope) and by same-process producers via <see cref="IIngestionExecutor"/>. Keeps
/// the quota check + provider/project re-hydration + processor dispatch in one place.
/// </summary>
internal sealed class IngestionExecutor : IIngestionExecutor
{
    private readonly IAgentCallProcessor processor;
    private readonly IRepository<IModelProvider> providerRepository;
    private readonly IRepository<IProject> projectRepository;
    private readonly ITraceQuotaGuard quotaGuard;
    private readonly ILogger<IngestionExecutor> logger;

    public IngestionExecutor(
        IAgentCallProcessor processor,
        IRepository<IModelProvider> providerRepository,
        IRepository<IProject> projectRepository,
        ITraceQuotaGuard quotaGuard,
        ILogger<IngestionExecutor> logger)
    {
        this.processor = processor;
        this.providerRepository = providerRepository;
        this.projectRepository = projectRepository;
        this.quotaGuard = quotaGuard;
        this.logger = logger;
    }

    public async Task IngestAsync(IngestMessage message, CancellationToken cancellationToken = default)
    {
        // Once the licensed monthly trace quota is reached, drop further captures rather than
        // persisting them. (The stream consumer still acks the dropped envelope to avoid redelivery.)
        //
        // Asked PER PROJECT, not installation-wide: the cap is global, but applying it as one switch
        // let a single busy project consume the month's allowance and silently stop capture for
        // every other project. Only projects above their share of the cap are dropped. The guard
        // raises a notification and logs at Error when a project starts being throttled, so this is
        // no longer invisible to the operator.
        if (quotaGuard.IsOverQuota(message.ProjectId))
        {
            logger.LogWarning("Monthly trace quota exceeded; dropping captured call for project {ProjectId}", message.ProjectId);
            return;
        }

        IModelProvider provider = await providerRepository.GetAsync(message.ProviderId, cancellationToken);
        IProject project = await projectRepository.GetAsync(message.ProjectId, cancellationToken);

        var job = new IngestJob(
            provider,
            project,
            message.RequestBody,
            message.ResponseBody,
            TimeSpan.FromMilliseconds(message.DurationMs),
            (HttpStatusCode)message.HttpStatus,
            message.SessionId,
            message.AgentName,
            message.BlockedByDetectorId,
            message.BlockedDetectorName,
            message.BlockedTriggerPattern,
            ConversationId: message.ConversationId);

        await processor.IngestAsync(job, cancellationToken);
    }
}
