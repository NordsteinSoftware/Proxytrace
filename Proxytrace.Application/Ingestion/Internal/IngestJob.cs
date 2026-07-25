using System.Net;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;

namespace Proxytrace.Application.Ingestion.Internal;

internal sealed record IngestJob(
    IModelProvider Provider,
    IProject Project,
    string RequestBody,
    string? ResponseBody,
    TimeSpan Duration,
    HttpStatusCode HttpStatus,
    string? SessionId = null,
    string? AgentName = null,
    Guid? BlockedByDetectorId = null,
    string? BlockedDetectorName = null,
    string? BlockedTriggerPattern = null,
    string? ConversationId = null);
