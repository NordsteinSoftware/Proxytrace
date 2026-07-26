using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;
using Proxytrace.Proxy.Controllers;
using Proxytrace.Proxy.Internal;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Monthly-budget enforcement at the controller level: once a project's hard limit is breached the
/// call must be rejected with an OpenAI-compatible 403 BEFORE any upstream contact, while still
/// publishing the blocked call to the ingestion stream. Uses the real <see cref="BudgetBlocker"/>
/// over a faked block provider, so the agent-scoping rules are exercised too.
/// </summary>
[TestClass]
public sealed class OpenAiProxyBudgetBlockingTests
{
    [TestMethod]
    public async Task Proxy_WithActiveProjectBlock_Returns403AndNeverContactsUpstream()
    {
        var upstream = new CapturingHttpMessageHandler("{}");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            BlocksOf(ProjectBlock()),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(body: """{"model":"gpt-4o","messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        upstream.LastMethod.Should().BeNull("a budget-blocked request must never reach the provider");
    }

    [TestMethod]
    public async Task Proxy_BudgetBlocked_ReturnsOpenAiCompatibleErrorJson()
    {
        var controller = BuildController(Substitute.For<IIngestionStream>(), BlocksOf(ProjectBlock()));
        controller.ControllerContext = BuildContext(body: """{"messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        using var doc = JsonDocument.Parse(ReadResponse(controller));
        JsonElement error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("proxytrace_budget_exceeded");
        error.GetProperty("type").GetString().Should().Be("invalid_request_error");
    }

    [TestMethod]
    public async Task Proxy_BudgetBlocked_ErrorBodyLeaksNoAmounts()
    {
        var controller = BuildController(Substitute.For<IIngestionStream>(), BlocksOf(ProjectBlock()));
        controller.ControllerContext = BuildContext(body: """{"messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        // An ingestion API key does not imply entitlement to the organisation's spend figures.
        string json = ReadResponse(controller);
        json.Should().NotContain("€");
        json.Should().NotMatchRegex(@"\d+([.,]\d+)?\s*(EUR|eur)");
    }

    [TestMethod]
    public async Task Proxy_BudgetBlocked_PublishesBlockedTraceFlaggedAsBudget()
    {
        var stream = Substitute.For<IIngestionStream>();
        var controller = BuildController(stream, BlocksOf(ProjectBlock()));
        controller.ControllerContext = BuildContext(body: """{"messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        await stream.Received(1).PublishAsync(
            Arg.Is<IngestMessage>(m => m != null
                && m.BlockedByBudget
                && m.HttpStatus == StatusCodes.Status403Forbidden
                && m.BlockedByDetectorId == null),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Proxy_WithAgentScopedBlockAndNoAgentHeader_IsForwarded()
    {
        var upstream = new CapturingHttpMessageHandler("{}");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            BlocksOf(AgentBlock("Support bot")),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(body: """{"messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        // Unattributed traffic is only caught by project-level budgets.
        upstream.LastMethod.Should().NotBeNull();
        controller.Response.StatusCode.Should().NotBe(StatusCodes.Status403Forbidden);
    }

    [TestMethod]
    public async Task Proxy_WithAgentScopedBlockAndMatchingHeader_Returns403()
    {
        var upstream = new CapturingHttpMessageHandler("{}");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            BlocksOf(AgentBlock("Support bot")),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(
            body: """{"messages":[]}""", agentName: "Support bot");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        upstream.LastMethod.Should().BeNull();
    }

    [TestMethod]
    public async Task Proxy_WithNoActiveBlocks_IsForwarded()
    {
        var upstream = new CapturingHttpMessageHandler("{}");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            BlocksOf(),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(body: """{"messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        upstream.LastMethod.Should().NotBeNull();
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static OpenAiProxyController BuildController(
        IIngestionStream stream,
        IBudgetBlockProvider blockProvider,
        IHttpClientFactory? httpClientFactory = null)
        => new(
            httpClientFactory ?? new FakeHttpClientFactory("{}"),
            stream,
            ResolverFor(ApiKey()),
            Substitute.For<IRequestBlocker>(),
            new BudgetBlocker(blockProvider),
            new KioskOptions(),
            new KioskEndpointOptions(),
            NullLogger<OpenAiProxyController>.Instance);

    private static BudgetHardBlock ProjectBlock()
        => new(Guid.NewGuid(), null, null);

    private static BudgetHardBlock AgentBlock(string agentName)
        => new(Guid.NewGuid(), Guid.NewGuid(), agentName);

    private static IBudgetBlockProvider BlocksOf(params BudgetHardBlock[] blocks)
    {
        var provider = Substitute.For<IBudgetBlockProvider>();
        provider.GetBlocksAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(blocks);
        return provider;
    }

    private static IApiKeyResolver ResolverFor(ResolvedApiKey resolved)
    {
        var resolver = Substitute.For<IApiKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(resolved);
        return resolver;
    }

    private static ResolvedApiKey ApiKey()
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Id.Returns(Guid.NewGuid());
        provider.Name.Returns("test-provider");
        provider.ApiKey.Returns("sk-upstream");
        provider.Endpoint.Returns(new Uri("http://upstream.test/"));

        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());

        return new ResolvedApiKey(project, provider);
    }

    private static string ReadResponse(ControllerBase controller)
        => Encoding.UTF8.GetString(((MemoryStream)controller.Response.Body).ToArray());

    private static ControllerContext BuildContext(string body, string? agentName = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer valid";
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.Method = "POST";
        httpContext.Response.Body = new MemoryStream();
        if (agentName is not null)
            httpContext.Request.Headers["x-proxytrace-agent"] = agentName;
        return new ControllerContext { HttpContext = httpContext };
    }
}
