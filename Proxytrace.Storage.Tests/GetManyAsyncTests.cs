using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Exceptions;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class GetManyAsyncTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetManyAsync_WithMissingId_Throws()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IUser>>();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var act = () => repository.GetManyAsync([user.Id, Guid.NewGuid()], CancellationToken);

        await act.Should().ThrowAsync<EntitiesNotFoundException>();
    }

    [TestMethod]
    public async Task GetManyAsync_WithIgnoreMissing_SkipsMissingIds()
    {
        // Backs FK-less JSON id lists (a suite's test cases, a run's results): a hard-deleted child
        // must not make the whole parent unmappable.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IUser>>();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var result = await repository.GetManyAsync([user.Id, Guid.NewGuid()], CancellationToken, ignoreMissing: true);

        result.Should().ContainSingle(u => u.Id == user.Id);
    }
}
