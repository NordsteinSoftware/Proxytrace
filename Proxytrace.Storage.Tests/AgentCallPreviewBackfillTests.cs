using Autofac.Features.OwnedInstances;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Message;
using Proxytrace.Storage.Internal;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AgentCallPreviewBackfillTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Backfill_RestoresPreviewForRowMissingIt()
    {
        IServiceProvider services = GetServices();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();

        var call = await gen.CreateAsync(CancellationToken);

        // Capture the preview ingestion computed, then null it to simulate a row written before the
        // RequestPreview column existed (it was added nullable, without a data migration).
        var expected = await ReadPreview(contextFactory, call.Id);
        await SetPreview(contextFactory, call.Id, null);

        // Bug reproduction: the list query reads the denormalised column, so the row shows no preview.
        var (before, _) = await repo.GetFilteredListAsync(new AgentCallFilter(), 1, 50, CancellationToken);
        before.Single(i => i.Id == call.Id).MessagePreview.Should().BeNull();

        var backfill = services.GetRequiredService<AgentCallPreviewBackfillService>();
        var updated = await backfill.BackfillAsync(CancellationToken);

        updated.Should().Be(1);
        var (after, _) = await repo.GetFilteredListAsync(new AgentCallFilter(), 1, 50, CancellationToken);
        // A request with a user message regains its exact preview; one without gets the empty marker.
        after.Single(i => i.Id == call.Id).MessagePreview.Should().Be(expected ?? string.Empty);
    }

    [TestMethod]
    public async Task Backfill_WhenEveryRowHasPreview_IsIdempotentNoOp()
    {
        IServiceProvider services = GetServices();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);

        var backfill = services.GetRequiredService<AgentCallPreviewBackfillService>();

        // Ingestion already populated the preview, so there is nothing to do — and a re-run stays a no-op.
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task Backfill_RequestWithoutUserMessage_WritesEmptyMarkerAndStaysIdempotent()
    {
        IServiceProvider services = GetServices();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();

        var call = await gen.CreateAsync(CancellationToken);

        // Shape a pre-column row whose request can never yield a preview: a system-only conversation
        // with a null preview. Build() returns null for it, so the backfill must mark it rather than
        // leave it null — otherwise the IS NULL candidate set never empties and the pass loops forever.
        await ReplaceRequestAndClearPreview(contextFactory, call.Id, new Conversation([Message.CreateSystemMessage("system only")]));

        var backfill = services.GetRequiredService<AgentCallPreviewBackfillService>();

        (await backfill.BackfillAsync(CancellationToken)).Should().Be(1);
        (await ReadPreview(contextFactory, call.Id)).Should().Be(string.Empty);

        // The empty marker takes the row out of the candidate set, so a re-run does nothing.
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task Backfill_ProcessesEveryRowAcrossMultipleBatches()
    {
        IServiceProvider services = GetServices();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();
        var ownedContextFactory = services.GetRequiredService<Func<Owned<StorageDbContext>>>();

        const int rowCount = 5;
        var ids = new List<Guid>();
        for (var i = 0; i < rowCount; i++)
        {
            var call = await gen.CreateAsync(CancellationToken);
            ids.Add(call.Id);
            await SetPreview(contextFactory, call.Id, null);
        }

        // batchSize 2 over 5 null rows = three iterations (2, 2, 1): exercises the pagination loop and
        // the "final partial batch" termination that a single-batch (0/1-row) test never reaches.
        var backfill = new AgentCallPreviewBackfillService(
            ownedContextFactory, NullLogger<AgentCallPreviewBackfillService>.Instance, batchSize: 2);

        (await backfill.BackfillAsync(CancellationToken)).Should().Be(rowCount);

        foreach (var id in ids)
        {
            (await ReadPreview(contextFactory, id)).Should().NotBeNull();
        }
    }

    [TestMethod]
    public async Task StartAsync_WhenBackfillKeepsFailing_SwallowsAndDoesNotBlockBoot()
    {
        // The backfill is best-effort: a persistent failure must be logged, not thrown, or it would
        // break host startup. A context factory that always throws drives every retry to failure.
        Func<Owned<StorageDbContext>> throwingFactory = () => throw new InvalidOperationException("database unavailable");
        var backfill = new AgentCallPreviewBackfillService(
            throwingFactory, NullLogger<AgentCallPreviewBackfillService>.Instance, retryDelay: TimeSpan.Zero);

        var act = async () => await backfill.StartAsync(CancellationToken);

        await act.Should().NotThrowAsync();
    }

    private static async Task ReplaceRequestAndClearPreview(Func<StorageDbContext> contextFactory, Guid id, Conversation request)
    {
        var db = contextFactory();
        var row = await db.Set<AgentCallEntity>().FirstAsync(e => e.Id == id);
        db.Entry(row).CurrentValues.SetValues(row with { Request = request, RequestPreview = null });
        await db.SaveChangesAsync();
    }

    private static async Task<string?> ReadPreview(Func<StorageDbContext> contextFactory, Guid id)
    {
        var db = contextFactory();
        var row = await db.Set<AgentCallEntity>().AsNoTracking().FirstAsync(e => e.Id == id);
        return row.RequestPreview;
    }

    private static async Task SetPreview(Func<StorageDbContext> contextFactory, Guid id, string? preview)
    {
        var db = contextFactory();
        var row = await db.Set<AgentCallEntity>().FirstAsync(e => e.Id == id);
        db.Entry(row).CurrentValues.SetValues(row with { RequestPreview = preview });
        await db.SaveChangesAsync();
    }
}
