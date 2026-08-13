using AwesomeAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Storage.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AmbientDbContextTests : BaseTest<Module>
{
    // Two concurrent logical units (e.g. the live stats drainer and the backfill worker) run
    // on independent async flows but share a single AmbientDbContext instance from the root
    // DI scope. Each must see only its own transactional context; otherwise their EF operations
    // collide ("A second operation was started on this context instance") and dispose races occur.
    [TestMethod]
    public async Task Set_IsIsolatedPerAsyncFlow()
    {
        var services = GetServices();
        var ambient = services.GetRequiredService<AmbientDbContext>();
        var contextA = services.GetRequiredService<StorageDbContext>();
        var contextB = services.GetRequiredService<StorageDbContext>();
        var txA = Substitute.For<IDbContextTransaction>();
        var txB = Substitute.For<IDbContextTransaction>();

        var aHasSet = new TaskCompletionSource();
        var bHasSet = new TaskCompletionSource();

        async Task FlowA()
        {
            ambient.Set(contextA, txA);
            aHasSet.SetResult();
            await bHasSet.Task;
            // Flow B set its own context meanwhile; flow A must still observe its own.
            ambient.Context.Should().BeSameAs(contextA);
        }

        async Task FlowB()
        {
            await aHasSet.Task;
            ambient.Set(contextB, txB);
            bHasSet.SetResult();
            ambient.Context.Should().BeSameAs(contextB);
        }

        await Task.WhenAll(Task.Run(FlowA), Task.Run(FlowB));
    }
}
