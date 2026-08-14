using System.Net;
using Autofac.Features.OwnedInstances;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Storage.Internal;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AgentCallToolBackfillTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Backfill_RestoresToolRowsForPreUpgradeTraces()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();

        var call = await SeedCallWithToolsAsync(services, agent, ["web_search", "get_weather"]);
        // Simulate a trace ingested before the tool-name table existed: ingestion has already
        // denormalised ResponseToolRequestCount, but no AgentCallToolEntity rows were written.
        await DeleteToolRows(contextFactory, call.Id);

        var backfill = services.GetRequiredService<AgentCallToolBackfillService>();
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(1);

        var rows = await ReadToolRows(contextFactory, call.Id);
        rows.Select(r => r.ToolName).Should().BeEquivalentTo(["web_search", "get_weather"]);
        rows.Should().OnlyContain(r => r.ProjectId == agent.Project.Id);
        // AgentId is denormalised on backfill too, so the agent-scoped picker sees pre-upgrade traces.
        rows.Should().OnlyContain(r => r.AgentId == agent.Id);
    }

    [TestMethod]
    public async Task Backfill_WhenEveryTraceHasToolRows_IsIdempotentNoOp()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        await SeedCallWithToolsAsync(services, agent, ["web_search"]);

        var backfill = services.GetRequiredService<AgentCallToolBackfillService>();

        // Ingestion already wrote the tool rows, so there is nothing to do — and a re-run stays a no-op.
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task Backfill_ResponseYieldingNoNames_WritesEmptyMarkerAndStaysIdempotent()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();

        var call = await SeedCallWithToolsAsync(services, agent, ["web_search"]);
        await DeleteToolRows(contextFactory, call.Id);
        // Corrupt-ish shape: the denormalised count says "has tools" but the stored response yields
        // none. The backfill must take the row out of the candidate set anyway (empty marker), or
        // the pass would re-scan it forever.
        await ReplaceResponse(contextFactory, call.Id, new AssistantMessage([Content.FromText("ok")], []));

        var backfill = services.GetRequiredService<AgentCallToolBackfillService>();

        (await backfill.BackfillAsync(CancellationToken)).Should().Be(1);
        var rows = await ReadToolRows(contextFactory, call.Id);
        rows.Should().ContainSingle().Which.ToolName.Should().Be(string.Empty);

        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task Backfill_EmptyMarkerRows_NeverSurfaceInToolNamePicker()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var call = await SeedCallWithToolsAsync(services, agent, ["web_search"]);
        await DeleteToolRows(contextFactory, call.Id);
        await ReplaceResponse(contextFactory, call.Id, new AssistantMessage([Content.FromText("ok")], []));
        await services.GetRequiredService<AgentCallToolBackfillService>().BackfillAsync(CancellationToken);

        (await repo.GetToolNamesAsync(agent.Project.Id, cancellationToken: CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Backfill_ProcessesEveryTraceAcrossMultipleBatches()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var contextFactory = services.GetRequiredService<Func<StorageDbContext>>();
        var ownedContextFactory = services.GetRequiredService<Func<Owned<StorageDbContext>>>();

        const int rowCount = 5;
        var ids = new List<Guid>();
        for (var i = 0; i < rowCount; i++)
        {
            var call = await SeedCallWithToolsAsync(services, agent, [$"tool_{i}", "shared_tool"]);
            ids.Add(call.Id);
            await DeleteToolRows(contextFactory, call.Id);
        }

        // batchSize 2 over 5 candidate rows = three iterations (2, 2, 1): exercises the batch loop
        // and the "final partial batch" termination a single-batch test never reaches.
        var backfill = new AgentCallToolBackfillService(
            ownedContextFactory, NullLogger<AgentCallToolBackfillService>.Instance, batchSize: 2);

        (await backfill.BackfillAsync(CancellationToken)).Should().Be(rowCount);

        foreach (var id in ids)
        {
            (await ReadToolRows(contextFactory, id)).Should().HaveCount(2);
        }
    }

    [TestMethod]
    public async Task StartAsync_WhenBackfillKeepsFailing_SwallowsAndDoesNotBlockBoot()
    {
        // Best-effort like the preview backfill: a persistent failure must be logged, not thrown,
        // or it would break host startup.
        Func<Owned<StorageDbContext>> throwingFactory = () => throw new InvalidOperationException("database unavailable");
        var backfill = new AgentCallToolBackfillService(
            throwingFactory, NullLogger<AgentCallToolBackfillService>.Instance, retryDelay: TimeSpan.Zero);

        var act = async () => await backfill.StartAsync(CancellationToken);

        await act.Should().NotThrowAsync();
    }

    private static async Task DeleteToolRows(Func<StorageDbContext> contextFactory, Guid callId)
    {
        var db = contextFactory();
        var rows = await db.Set<AgentCallToolEntity>().Where(t => t.AgentCallId == callId).ToListAsync();
        db.Set<AgentCallToolEntity>().RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<List<AgentCallToolEntity>> ReadToolRows(Func<StorageDbContext> contextFactory, Guid callId)
    {
        var db = contextFactory();
        return await db.Set<AgentCallToolEntity>().AsNoTracking().Where(t => t.AgentCallId == callId).ToListAsync();
    }

    private static async Task ReplaceResponse(Func<StorageDbContext> contextFactory, Guid id, AssistantMessage response)
    {
        var db = contextFactory();
        var row = await db.Set<AgentCallEntity>().FirstAsync(e => e.Id == id);
        db.Entry(row).CurrentValues.SetValues(row with { Response = response });
        await db.SaveChangesAsync();
    }

    private async Task<IAgentCall> SeedCallWithToolsAsync(
        IServiceProvider services,
        IAgent agent,
        IReadOnlyList<string> toolNames)
    {
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var request = await conversationGen.CreateAsync(CancellationToken);

        var assistantMessage = new AssistantMessage(
            [Content.FromText("ok")],
            toolNames.Select((name, i) => new ToolRequest($"tr{i}", name, "{}")).ToList());
        ICompletion response = createCompletion(assistantMessage, new TokenUsage(10, 10), TimeSpan.FromMilliseconds(50));

        IAgentCall call = services.GetRequiredService<IAgentCall.CreateNew>()(
            agent,
            agent.CurrentVersion,
            agent.Endpoint,
            request,
            response,
            httpStatus: HttpStatusCode.OK,
            finishReason: "stop",
            errorMessage: null,
            modelParameters: agent.ModelParameters);

        var repo = services.GetRequiredService<IAgentCallRepository>();
        return await repo.AddAsync(call, CancellationToken);
    }
}
