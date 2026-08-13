using System.Net;
using AwesomeAssertions;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.CustomAnomaly;
using Proxytrace.Application.Ingestion;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.CustomAnomaly;

/// <summary>
/// The ingestion-side enqueue hook: every persisted call of a regular agent is queued for
/// custom-anomaly review; system agents' traffic (evaluator judges, Tracey) is not.
/// </summary>
[TestClass]
public sealed class CustomAnomalyEnqueueHookTests : BaseTest<Module>
{
    private const string RequestBody = """
                                       {
                                           "model": "gpt-4o",
                                           "messages": [
                                               {"role": "system", "content": "You are a support agent."},
                                               {"role": "user", "content": "I demand a refund immediately!"}
                                           ]
                                       }
                                       """;

    private const string ResponseBody = """
                                        {
                                            "id": "chatcmpl-1",
                                            "object": "chat.completion",
                                            "model": "gpt-4o",
                                            "choices": [{
                                                "index": 0,
                                                "message": {"role": "assistant", "content": "Certainly, refund granted."},
                                                "finish_reason": "stop"
                                            }],
                                            "usage": {"prompt_tokens": 7, "completion_tokens": 9, "total_tokens": 16}
                                        }
                                        """;

    private async Task<(IServiceProvider Services, ICustomAnomalyReviewQueue Queue, IModelProvider Provider, IProject Project)>
        BuildAsync()
    {
        var queue = Substitute.For<ICustomAnomalyReviewQueue>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(queue).As<ICustomAnomalyReviewQueue>());

        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>()
            .GetOrCreateAsync(CancellationToken);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .GetOrCreateAsync(CancellationToken);
        return (services, queue, provider, project);
    }

    [TestMethod]
    public async Task IngestAsync_RegularAgentCall_EnqueuesPersistedCallForReview()
    {
        var (services, queue, provider, project) = await BuildAsync();
        var executor = services.GetRequiredService<IIngestionExecutor>();

        await executor.IngestAsync(
            new IngestMessage(
                ProviderId: provider.Id,
                ProjectId: project.Id,
                RequestBody: RequestBody,
                ResponseBody: ResponseBody,
                DurationMs: 100,
                HttpStatus: (int)HttpStatusCode.OK,
                SessionId: null),
            CancellationToken);

        var callRepo = services.GetRequiredService<IAgentCallRepository>();
        var call = (await callRepo.GetAllAsync(CancellationToken)).Should().ContainSingle().Subject;
        await queue.Received(1).EnqueueAsync(call.Id, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task IngestAsync_SystemAgentCall_DoesNotEnqueueForReview()
    {
        var (services, queue, provider, project) = await BuildAsync();
        var executor = services.GetRequiredService<IIngestionExecutor>();

        // Pre-provision a system agent and attribute the capture to it by name (the
        // X-Proxytrace-Agent path) — internal system traffic must not be reviewed.
        var systemAgent = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("Internal Helper", isSystemAgent: true, cancellationToken: CancellationToken);
        systemAgent.Project.Id.Should().Be(project.Id);

        await executor.IngestAsync(
            new IngestMessage(
                ProviderId: provider.Id,
                ProjectId: project.Id,
                RequestBody: RequestBody,
                ResponseBody: ResponseBody,
                DurationMs: 100,
                HttpStatus: (int)HttpStatusCode.OK,
                SessionId: null,
                AgentName: systemAgent.Name),
            CancellationToken);

        var callRepo = services.GetRequiredService<IAgentCallRepository>();
        (await callRepo.GetAllAsync(CancellationToken)).Should().ContainSingle();
        await queue.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
