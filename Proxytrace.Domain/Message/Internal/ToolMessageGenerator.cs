using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Message.Internal;

internal class ToolMessageGenerator : DomainObjectGenerator<ToolMessage>
{
    private readonly IDomainObjectGenerator<ToolResponse> toolResponseGenerator;

    public ToolMessageGenerator(
        IRandom random,
        IDomainObjectGenerator<ToolResponse> toolResponseGenerator) : base(random)
    {
        this.toolResponseGenerator = toolResponseGenerator;
    }

    public override async Task<ToolMessage> CreateAsync(CancellationToken cancellationToken = default)
    {
        var response = await toolResponseGenerator.CreateAsync(cancellationToken);
        return new ToolMessage(response);
    }
}
