using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;
using Proxytrace.Proxy.Controllers;
using Proxytrace.Proxy.Internal;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// Real-time blocking detectors at the controller level: a trigger match against the request body
/// must reject the call with an OpenAI-compatible 403 BEFORE any upstream contact, while still
/// publishing the blocked call to the ingestion stream. Uses the real <see cref="RequestBlocker"/>
/// over a faked rule provider, so the agent-scoping rules are exercised too.
/// </summary>
[TestClass]
public sealed class OpenAiProxyBlockingTests
{
    [TestMethod]
    public async Task Proxy_AllAgentsPhraseMatch_Returns403AndNeverContactsUpstream()
    {
        var upstream = new CapturingHttpMessageHandler("{}");
        var stream = Substitute.For<IIngestionStream>();
        var controller = BuildController(
            stream,
            RulesFor(Rule("Secret guard", allAgents: true, Phrase("hunter2"))),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(
            body: """{"model":"gpt-4o","messages":[{"role":"user","content":"my password is hunter2"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        upstream.LastMethod.Should().BeNull("a blocked request must never reach the upstream provider");
    }

    [TestMethod]
    public async Task Proxy_Blocked_ReturnsOpenAiCompatibleErrorJson()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            RulesFor(Rule("Secret guard", allAgents: true, Phrase("hunter2"))));
        controller.ControllerContext = BuildContext(body: """{"messages":[{"content":"hunter2"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        var json = ReadResponse(controller);
        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("proxytrace_blocked");
        error.GetProperty("type").GetString().Should().Be("invalid_request_error");
        error.GetProperty("message").GetString().Should().Contain("Secret guard");
        controller.Response.ContentType.Should().StartWith("application/json");
    }

    [TestMethod]
    public async Task Proxy_Blocked_PublishesBlockedIngestMessage()
    {
        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var rule = Rule("Secret guard", allAgents: true, Phrase("hunter2"));
        var controller = BuildController(stream, RulesFor(rule));
        controller.ControllerContext = BuildContext(body: """{"messages":[{"content":"hunter2"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        captured.Should().NotBeNull();
        captured.BlockedByDetectorId.Should().Be(rule.DetectorId);
        captured.BlockedDetectorName.Should().Be("Secret guard");
        captured.BlockedTriggerPattern.Should().Be("hunter2");
        captured.HttpStatus.Should().Be(StatusCodes.Status403Forbidden);
        captured.ResponseBody.Should().Contain("proxytrace_blocked");
        captured.RequestBody.Should().Contain("hunter2", "the blocked trace still records what was attempted");
    }

    [TestMethod]
    public async Task Proxy_RegexTrigger_BlocksSecretPattern()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            RulesFor(Rule("API key guard", allAgents: true,
                new AnomalyTrigger(TriggerKind.Regex, "sk-[a-z0-9]{20,}"))));
        controller.ControllerContext = BuildContext(
            body: """{"messages":[{"content":"use sk-abcdefghij0123456789x please"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [TestMethod]
    public async Task Proxy_ScopedDetectorWithMatchingAgentHeader_Blocks()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            RulesFor(Rule("Scoped guard", allAgents: false, Phrase("hunter2"), "Billing Agent")));
        controller.ControllerContext = BuildContext(body: """{"messages":[{"content":"hunter2"}]}""");
        controller.ControllerContext.HttpContext.Request.Headers["x-proxytrace-agent"] = "billing agent";

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(
            StatusCodes.Status403Forbidden, "the agent-name header matches a scoped agent case-insensitively");
    }

    [TestMethod]
    public async Task Proxy_ScopedDetectorWithoutAgentHeader_Forwards()
    {
        var upstream = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            RulesFor(Rule("Scoped guard", allAgents: false, Phrase("hunter2"), "Billing Agent")),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(body: """{"messages":[{"content":"hunter2"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        // No pre-upstream attribution signal -> the proxy cannot tell whether the scoped agent is
        // calling, so it forwards; the post-ingestion review pipeline still flags the call.
        upstream.LastMethod.Should().NotBeNull("an unattributed request must not match an agent-scoped rule");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [TestMethod]
    public async Task Proxy_StreamingRequest_IsBlockedBeforeUpstream()
    {
        var upstream = new CapturingHttpMessageHandler("data: [DONE]\n");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            RulesFor(Rule("Secret guard", allAgents: true, Phrase("hunter2"))),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(
            body: """{"model":"gpt-4o","stream":true,"messages":[{"content":"hunter2"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        upstream.LastMethod.Should().BeNull("the body is fully buffered, so streaming blocks identically");
    }

    [TestMethod]
    public async Task Proxy_NoTriggerMatch_ForwardsNormally()
    {
        var stream = Substitute.For<IIngestionStream>();
        var controller = BuildController(
            stream,
            RulesFor(Rule("Secret guard", allAgents: true, Phrase("hunter2"))),
            new FakeHttpClientFactory(FakeHttpMessageHandler.BuildOpenAiResponse("hello")));
        controller.ControllerContext = BuildContext(body: """{"messages":[{"content":"all good"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        await stream.Received(1).PublishAsync(
            Arg.Is<IngestMessage>(m => m != null && m.BlockedByDetectorId == null), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Proxy_NoRules_ForwardsNormally()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            RulesFor(),
            new FakeHttpClientFactory(FakeHttpMessageHandler.BuildOpenAiResponse("hello")));
        controller.ControllerContext = BuildContext(body: """{"messages":[{"content":"hunter2"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [TestMethod]
    public async Task Passthrough_NeverBlocks()
    {
        var upstream = new CapturingHttpMessageHandler("{}");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            RulesFor(Rule("Secret guard", allAgents: true, Phrase("hunter2"))),
            new SingleHandlerClientFactory(upstream));
        controller.ControllerContext = BuildContext(body: """{"data":"hunter2"}""");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        // The passthrough surface is non-LLM and never ingested; a block there could not be
        // recorded as a trace, so blocking applies to the traced Proxy() surface only.
        upstream.LastMethod.Should().NotBeNull();
        controller.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static OpenAiProxyController BuildController(
        IIngestionStream stream,
        IBlockingRuleProvider ruleProvider,
        IHttpClientFactory? httpClientFactory = null)
        => new(
            httpClientFactory ?? new FakeHttpClientFactory("{}"),
            stream,
            ResolverFor(ApiKey()),
            new RequestBlocker(ruleProvider),
            Substitute.For<IBudgetBlocker>(),
            new KioskOptions(),
            new KioskEndpointOptions(),
            NullLogger<OpenAiProxyController>.Instance);

    private static AnomalyTrigger Phrase(string pattern)
        => new(TriggerKind.Phrase, pattern);

    private static BlockingDetectorRule Rule(
        string name, bool allAgents, AnomalyTrigger trigger, params string[] scopedAgentNames)
        => new(Guid.NewGuid(), name, [trigger], allAgents, scopedAgentNames);

    private static IBlockingRuleProvider RulesFor(params BlockingDetectorRule[] rules)
    {
        var provider = Substitute.For<IBlockingRuleProvider>();
        provider.GetRulesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(rules);
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

    private static ControllerContext BuildContext(string body)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer valid";
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.Method = "POST";
        httpContext.Response.Body = new MemoryStream();
        return new ControllerContext { HttpContext = httpContext };
    }
}
