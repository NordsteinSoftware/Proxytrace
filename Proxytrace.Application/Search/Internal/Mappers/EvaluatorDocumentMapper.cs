using System.Text;
using Lucene.Net.Documents;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal.Mappers;

internal sealed class EvaluatorDocumentMapper : AbstractDocumentMapper<IEvaluator>
{
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override SearchKind Kind => SearchKind.Evaluator;

    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluatorDocumentMapper"/> class.
    /// </summary>
    public EvaluatorDocumentMapper(
        IRepository<IEvaluator> repository,
        ILogger<EvaluatorDocumentMapper> logger) : base(repository, logger)
    {
    }

    protected override Document? GetDocument(IEvaluator evaluator)
    {
        if (evaluator is not IAgenticEvaluator agenticEvaluator)
        {
            return null;
        }
        
        var body = new StringBuilder()
            .Append(agenticEvaluator.Agent.SystemPrompt.Name).Append('\n')
            .Append(agenticEvaluator.Agent.SystemPrompt.Template);

        var title = !string.IsNullOrEmpty(evaluator.Name)
            ? agenticEvaluator.Name
            : agenticEvaluator.Agent.SystemPrompt.Name;

        return DocumentBuilder.Build(
            kind: SearchKind.Evaluator,
            entityId: agenticEvaluator.Id,
            projectId: agenticEvaluator.Project.Id,
            createdAt: agenticEvaluator.CreatedAt,
            title: title,
            body: body.ToString(),
            boostedBody: title);
    }
}
