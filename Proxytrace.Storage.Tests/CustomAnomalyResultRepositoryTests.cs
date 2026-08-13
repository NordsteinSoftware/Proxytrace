using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.CustomAnomaly;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class CustomAnomalyResultRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AddAsync_RoundTripsAllFields()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyResultRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyResult.CreateNew>();
        var detector = await services.GetRequiredService<IDomainEntityGenerator<ICustomAnomalyDetector>>()
            .CreateAsync(CancellationToken);
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>()
            .CreateAsync(CancellationToken);

        var result = await repo.AddAsync(
            factory(detector.Id, call.Id, call.Agent.Project.Id, "refund", "Unauthorized refund promise."),
            CancellationToken);

        var reloaded = await repo.GetAsync(result.Id, CancellationToken);
        reloaded.DetectorId.Should().Be(detector.Id);
        reloaded.AgentCallId.Should().Be(call.Id);
        reloaded.ProjectId.Should().Be(call.Agent.Project.Id);
        reloaded.MatchedTrigger.Should().Be("refund");
        reloaded.Reasoning.Should().Be("Unauthorized refund promise.");
    }

    [TestMethod]
    public async Task GetByAgentCallIdsAsync_ReturnsOnlyResultsOfRequestedCalls()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyResultRepository>();
        var factory = services.GetRequiredService<ICustomAnomalyResult.CreateNew>();
        var detector = await services.GetRequiredService<IDomainEntityGenerator<ICustomAnomalyDetector>>()
            .CreateAsync(CancellationToken);
        var callGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var callA = await callGenerator.CreateAsync(CancellationToken);
        var callB = await callGenerator.CreateAsync(CancellationToken);
        var callC = await callGenerator.CreateAsync(CancellationToken);

        var resultA = await repo.AddAsync(
            factory(detector.Id, callA.Id, callA.Agent.Project.Id, "refund", null), CancellationToken);
        var resultB = await repo.AddAsync(
            factory(detector.Id, callB.Id, callB.Agent.Project.Id, "lawsuit", null), CancellationToken);
        await repo.AddAsync(
            factory(detector.Id, callC.Id, callC.Agent.Project.Id, "escalation", null), CancellationToken);

        var results = await repo.GetByAgentCallIdsAsync([callA.Id, callB.Id], CancellationToken);

        results.Select(r => r.Id).Should().BeEquivalentTo([resultA.Id, resultB.Id]);
    }

    [TestMethod]
    public async Task GetByAgentCallIdsAsync_EmptyInput_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ICustomAnomalyResultRepository>();

        var results = await repo.GetByAgentCallIdsAsync([], CancellationToken);

        results.Should().BeEmpty();
    }
}
