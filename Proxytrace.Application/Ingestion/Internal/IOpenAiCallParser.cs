using System.Net;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.Ingestion.Internal;

internal sealed record ParseResult(
    IModelEndpoint Endpoint,
    Conversation Request,
    ICompletion? Response,
    HttpStatusCode HttpStatus,
    string? FinishReason,
    string? ErrorMessage,
    SystemMessage SystemMessage,
    IReadOnlyList<ToolSpecification> Tools,
    IModelParameters ModelParameters);


internal interface IOpenAiCallParser
{
    Task<ParseResult?> TryParse(IModelProvider provider,
        string requestBody,
        string? responseBody,
        TimeSpan duration,
        HttpStatusCode httpStatus,
        CancellationToken cancellationToken = default);
}
