using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Demo.Internal;
using Proxytrace.Application.Demo.Scenarios;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Demo;

[TestClass]
public class KioskLiveTrafficServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task EmitInteraction_PersistsFreshCalls_ForASeededDemoAgent()
    {
        var broadcaster = Substitute.For<ITraceBroadcaster>();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(new KioskOptions { Enabled = true }).AsSelf();
            builder.RegisterInstance(broadcaster).As<ITraceBroadcaster>();
        });

        await services.GetRequiredService<CoreSeedScenario>().SeedAsync(CancellationToken);
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();
        int before = (await callRepo.GetAllAsync(CancellationToken)).Count;

        var sut = services.GetRequiredService<KioskLiveTrafficService>();
        await sut.EmitInteractionAsync(CancellationToken);

        var after = await callRepo.GetAllAsync(CancellationToken);
        // One emission is a single call or a two-call tool round-trip.
        (after.Count - before).Should().BeInRange(1, 2);

        var fresh = after.OrderByDescending(c => c.CreatedAt).Take(after.Count - before).ToList();
        fresh.Should().OnlyContain(
            c => c.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1),
            "live traffic must land 'now' so the dashboard's pulse and live telemetry pick it up");
    }

    [TestMethod]
    public async Task EmitInteraction_BroadcastsATraceCreatedEvent_PerPersistedCall()
    {
        var broadcaster = Substitute.For<ITraceBroadcaster>();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(new KioskOptions { Enabled = true }).AsSelf();
            builder.RegisterInstance(broadcaster).As<ITraceBroadcaster>();
        });

        await services.GetRequiredService<CoreSeedScenario>().SeedAsync(CancellationToken);
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();
        int before = (await callRepo.GetAllAsync(CancellationToken)).Count;

        var sut = services.GetRequiredService<KioskLiveTrafficService>();
        await sut.EmitInteractionAsync(CancellationToken);

        int added = (await callRepo.GetAllAsync(CancellationToken)).Count - before;
        broadcaster.Received(added).Publish(Arg.Any<TraceCreatedEvent>());
    }
}
