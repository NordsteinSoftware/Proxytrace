using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.Message.Internal;

internal class ToolResponseGenerator : DomainObjectGenerator<ToolResponse>
{
    private readonly IDomainObjectGenerator<ToolRequest> toolRequestGenerator;
    private readonly IDomainObjectGenerator<Content> contentGenerator;

    public ToolResponseGenerator(
        IRandom random,
        IDomainObjectGenerator<ToolRequest> toolRequestGenerator,
        IDomainObjectGenerator<Content> contentGenerator) : base(random)
    {
        this.toolRequestGenerator = toolRequestGenerator;
        this.contentGenerator = contentGenerator;
    }

    public override async Task<ToolResponse> CreateAsync(CancellationToken cancellationToken = default)
    {
        var request = await toolRequestGenerator.CreateAsync(cancellationToken);
        var result = await contentGenerator.CreateAsync(cancellationToken);
        return new ToolResponse(request, [result]);
    }
}
