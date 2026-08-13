using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.Prompt.Internal;

internal sealed class PromptTemplateGenerator : DomainObjectGenerator<IPromptTemplate>
{
    private readonly IPromptTemplate.Create factory;

    public PromptTemplateGenerator(
        IRandom random,
        IPromptTemplate.Create factory) : base(random)
    {
        this.factory = factory;
    }

    public override Task<IPromptTemplate> CreateAsync(CancellationToken cancellationToken = default) 
        => factory(name: random.String(), template: random.String())
            .ToTaskResult();
}