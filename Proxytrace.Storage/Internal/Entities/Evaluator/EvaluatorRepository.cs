using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.Domain.Events;
using Proxytrace.Storage.Internal.Entities.TestSuite;

namespace Proxytrace.Storage.Internal.Entities.Evaluator;

[UsedImplicitly]
internal class EvaluatorRepository : ArchivableRepository<IEvaluator, EvaluatorEntity>, IEvaluatorRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluatorRepository"/> class.
    /// </summary>
    public EvaluatorRepository(
        IMapper<IEvaluator, EvaluatorEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    /// <summary>
    /// Gets the by project asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<IEvaluator>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<EvaluatorEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId)
            .ExcludeArchived()
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the project id asynchronously.
    /// </summary>
    public async Task<Guid?> GetProjectIdAsync(Guid evaluatorId, CancellationToken cancellationToken = default)
        => await contextFactory()
            .Set<EvaluatorEntity>()
            .AsNoTracking()
            .Where(e => e.Id == evaluatorId)
            .Select(e => (Guid?)e.Project)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Detaches the evaluator from every test suite on archive so future suite runs stop using it.
    /// Past test results keep resolving it by id (the row stays), so history is preserved.
    /// </summary>
    protected override Task ArchiveRelationsAsync(
        DbContext context,
        Guid id,
        CancellationToken cancellationToken)
        => transaction.InvokeAsync(async () =>
        {
            var junctions = context.Set<TestSuiteEvaluatorEntity>().Where(e => e.EvaluatorId == id);

            if (context.Database.IsRelational())
            {
                await junctions.ExecuteDeleteAsync(cancellationToken);
                return;
            }

            // The in-memory provider does not support ExecuteDelete — materialize then remove.
            context.Set<TestSuiteEvaluatorEntity>().RemoveRange(await junctions.ToListAsync(cancellationToken));
        });
}
