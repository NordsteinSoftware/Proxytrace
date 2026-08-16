using System.Text;
using Lucene.Net.Documents;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal.Mappers;

internal sealed class AgentDocumentMapper : AbstractDocumentMapper<IAgent>
{
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override SearchKind Kind => SearchKind.Agent;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentDocumentMapper"/> class.
    /// </summary>
    public AgentDocumentMapper(
        IRepository<IAgent> repository,
        ILogger<AgentCallDocumentMapper> logger) : base(repository, logger)
    {
    }
    
    protected override Document? GetDocument(IAgent agent)
    {
        // System agents are internal plumbing (Tracey, the prompt/A-B optimizers, agentic-evaluator
        // agents) — never something a user searches for. Returning null keeps them out of the index
        // and, because FlushBatchAsync deletes the entry when BuildAsync returns null, purges any
        // already-indexed system agent on the next update/reindex.
        if (agent.IsSystemAgent)
        {
            return null;
        }

        var body = new StringBuilder()
            .Append(agent.SystemPrompt.Name).Append('\n')
            .Append(agent.SystemPrompt.Template).Append('\n');
        var boosted = new StringBuilder().Append(agent.Name);
        foreach (var tool in agent.Tools)
        {
            boosted.Append(' ').Append(tool.Name);
            body.Append(tool.Name).Append('\n').Append(tool.Description).Append('\n');
        }

        return DocumentBuilder.Build(
            kind: SearchKind.Agent,
            entityId: agent.Id,
            projectId: agent.Project.Id,
            createdAt: agent.CreatedAt,
            title: agent.Name,
            body: body.ToString(),
            boostedBody: boosted.ToString());
    }
}
