using Nordstein.Core.Common.Random;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.CostLimitBreach.Internal;

internal class CostLimitBreachGenerator : DomainEntityGenerator<ICostLimitBreach>
{
    private readonly ICostLimitBreach.CreateNew factory;
    private readonly IDomainEntityGenerator<ICostLimit> costLimitGenerator;

    public CostLimitBreachGenerator(
        ICostLimitBreach.CreateNew factory,
        IRepository<ICostLimitBreach> repository,
        IDomainEntityGenerator<ICostLimit> costLimitGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.costLimitGenerator = costLimitGenerator;
    }

    public override async Task<ICostLimitBreach> GenerateAsync(CancellationToken cancellationToken = default)
    {
        ICostLimit costLimit = await costLimitGenerator.GetOrCreateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return factory(
            costLimit: costLimit,
            monthStart: new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero),
            threshold: CostThreshold.Soft,
            spendEur: costLimit.SoftLimitEur ?? 1m);
    }
}
