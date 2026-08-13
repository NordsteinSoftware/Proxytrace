using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Prompt.Internal;

internal sealed class PromptGenerator : DomainObjectGenerator<IPrompt>
{
    private readonly IDomainObjectGenerator<IPromptTemplate> templateGenerator;

    public PromptGenerator(
        IRandom random,
        IDomainObjectGenerator<IPromptTemplate> templateGenerator) : base(random)
    {
        this.templateGenerator = templateGenerator;
    }

    public override async Task<IPrompt> CreateAsync(CancellationToken cancellationToken = default)
    {
        IPromptTemplate template = await templateGenerator.CreateAsync(cancellationToken);
        return template.Render();
    }
}
