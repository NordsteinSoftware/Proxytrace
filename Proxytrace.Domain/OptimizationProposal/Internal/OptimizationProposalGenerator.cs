using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.OptimizationProposal.Internal;

internal class OptimizationProposalGenerator : DomainEntityGenerator<IOptimizationProposal>, IOptimizationProposalGenerator
{
    private readonly IDomainEntityGenerator<ISystemPromptProposal> systemPromptGenerator;
    private readonly IDomainEntityGenerator<IToolUpdateProposal> toolUpdateGenerator;
    private readonly IDomainEntityGenerator<IModelSwitchProposal> modelSwitchGenerator;

    public OptimizationProposalGenerator(
        IRepository<IOptimizationProposal> repository,
        IDomainEntityGenerator<ISystemPromptProposal> systemPromptGenerator,
        IDomainEntityGenerator<IToolUpdateProposal> toolUpdateGenerator,
        IDomainEntityGenerator<IModelSwitchProposal> modelSwitchGenerator,
        IRandom random) : base(repository, random)
    {
        this.systemPromptGenerator = systemPromptGenerator;
        this.toolUpdateGenerator = toolUpdateGenerator;
        this.modelSwitchGenerator = modelSwitchGenerator;
    }

    public override async Task<IOptimizationProposal> GenerateAsync(CancellationToken cancellationToken = default)
        => await systemPromptGenerator.GenerateAsync(cancellationToken);

    public async Task<IOptimizationProposal> CreateAsync(ProposalKind kind, CancellationToken cancellationToken = default)
        => kind switch
        {
            ProposalKind.SystemPrompt => await systemPromptGenerator.CreateAsync(cancellationToken),
            ProposalKind.Tool => await toolUpdateGenerator.CreateAsync(cancellationToken),
            ProposalKind.ModelSwitch => await modelSwitchGenerator.CreateAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
