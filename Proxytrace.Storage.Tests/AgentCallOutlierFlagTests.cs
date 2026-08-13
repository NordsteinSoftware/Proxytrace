using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AgentCallOutlierFlagTests : BaseTest<Module>
{
    private static async Task<IAgentCall> CreateCallWithFlags(
        IServiceProvider services,
        OutlierFlags flags,
        CancellationToken cancellationToken)
    {
        var template = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>()
            .GenerateAsync(cancellationToken);
        var factory = services.GetRequiredService<IAgentCall.CreateNew>();
        var call = factory(
            agent: template.Agent,
            version: template.Version,
            endpoint: template.Endpoint,
            request: template.Request,
            response: template.Response,
            httpStatus: HttpStatusCode.OK,
            outlierFlags: flags);
        return await services.GetRequiredService<IAgentCallRepository>().AddAsync(call, cancellationToken);
    }

    [TestMethod]
    public async Task SetOutlierFlagAsync_OnUnflaggedCall_SetsBit()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var call = await CreateCallWithFlags(services, OutlierFlags.None, CancellationToken);

        await repo.SetOutlierFlagAsync(call.Id, OutlierFlags.CustomAnomaly, CancellationToken);

        var reloaded = await repo.GetAsync(call.Id, CancellationToken);
        reloaded.OutlierFlags.Should().Be(OutlierFlags.CustomAnomaly);
    }

    [TestMethod]
    public async Task SetOutlierFlagAsync_PreservesExistingBits()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var call = await CreateCallWithFlags(
            services, OutlierFlags.HighTokens | OutlierFlags.HighLatency, CancellationToken);

        await repo.SetOutlierFlagAsync(call.Id, OutlierFlags.CustomAnomaly, CancellationToken);

        var reloaded = await repo.GetAsync(call.Id, CancellationToken);
        reloaded.OutlierFlags.Should().Be(
            OutlierFlags.HighTokens | OutlierFlags.HighLatency | OutlierFlags.CustomAnomaly);
    }

    [TestMethod]
    public async Task SetOutlierFlagAsync_IsIdempotent()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var call = await CreateCallWithFlags(services, OutlierFlags.None, CancellationToken);

        await repo.SetOutlierFlagAsync(call.Id, OutlierFlags.CustomAnomaly, CancellationToken);
        await repo.SetOutlierFlagAsync(call.Id, OutlierFlags.CustomAnomaly, CancellationToken);

        var reloaded = await repo.GetAsync(call.Id, CancellationToken);
        reloaded.OutlierFlags.Should().Be(OutlierFlags.CustomAnomaly);
    }

    [TestMethod]
    public async Task SetOutlierFlagAsync_UnknownCall_IsNoOp()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var act = () => repo.SetOutlierFlagAsync(Guid.NewGuid(), OutlierFlags.CustomAnomaly, CancellationToken);

        await act.Should().NotThrowAsync();
    }
}
