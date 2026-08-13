using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.TestSuite;

internal class TestSuiteConfig : AbstractEntityConfiguration<TestSuiteEntity>, IMapper<ITestSuite, TestSuiteEntity>
{
    private readonly IRepository<IAgent> agents;
    private readonly IRepository<IEvaluator> evaluators;
    private readonly IRepository<ITestCase> testCases;
    private readonly ITestSuite.CreateExisting factory;
    private readonly ISerializer serializer;
    private readonly Func<StorageDbContext> contextFactory;

    public TestSuiteConfig(
        IRepository<IAgent> agents,
        IRepository<IEvaluator> evaluators,
        IRepository<ITestCase> testCases,
        ITestSuite.CreateExisting factory,
        ISerializer serializer,
        Func<StorageDbContext> contextFactory)
    {
        this.agents = agents;
        this.evaluators = evaluators;
        this.testCases = testCases;
        this.factory = factory;
        this.serializer = serializer;
        this.contextFactory = contextFactory;
    }

    public override void Configure(EntityTypeBuilder<TestSuiteEntity> builder)
    {
        builder
            .HasOne<AgentEntity>()
            .WithMany()
            .HasForeignKey(e => e.Agent)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .Property(e => e.TestCases)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.Deserialize<IReadOnlyCollection<Guid>>(v) ?? Array.Empty<Guid>()
            );
    }

    public async Task<ITestSuite> Map(TestSuiteEntity storedEntity, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var evaluatorIds = await context.Set<TestSuiteEvaluatorEntity>()
            .AsNoTracking()
            .Where(e => e.TestSuiteId == storedEntity.Id)
            .Select(e => e.EvaluatorId)
            .ToListAsync(cancellationToken);

        var loadedEvaluators = evaluatorIds.Count > 0
            ? await evaluators.GetManyAsync(evaluatorIds, cancellationToken)
            : [];

        return factory(
            name: storedEntity.Name,
            agent: await agents.GetAsync(storedEntity.Agent, cancellationToken),
            evaluators: loadedEvaluators,
            testCases: await testCases.GetManyAsync(storedEntity.TestCases, cancellationToken, ignoreMissing: true),
            existing: storedEntity);
    }

    public Task<TestSuiteEntity> Map(ITestSuite domainEntity, CancellationToken cancellationToken = default)
        => new TestSuiteEntity
        {
            Id = domainEntity.Id,
            Name = domainEntity.Name,
            Agent = domainEntity.Agent.Id,
            TestCases = domainEntity.TestCases.Select(x => x.Id).ToArray(),
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            TestSuiteEvaluators = domainEntity.Evaluators
                .Select(e => new TestSuiteEvaluatorEntity { TestSuiteId = domainEntity.Id, EvaluatorId = e.Id })
                .ToList()
        }.ToTaskResult();
}
