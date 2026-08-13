using JetBrains.Annotations;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Storage.Internal.Entities.TestCase;

[UsedImplicitly]
internal class TestCaseRepository : AbstractRepository<ITestCase, TestCaseEntity>, ITestCaseRepository
{
    public TestCaseRepository(
        IMapper<ITestCase, TestCaseEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }
}
