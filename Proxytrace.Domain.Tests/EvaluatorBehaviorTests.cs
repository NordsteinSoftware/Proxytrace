using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Evaluator.Internal;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class EvaluatorBehaviorTests : BaseTest<Module>
{
    [TestMethod]
    public async Task JsonSchemaMatch_ValidResponse_ScoresExcellent()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IJsonSchemaMatchEvaluator>>()
            .CreateAsync(CancellationToken);
        var testResult = BuildResult(actual: """{"name":"foo","age":5}""");

        var concrete = new JsonSchemaMatchEvaluator(
            jsonSchema: """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""",
            project: evaluator.Project,
            evaluationFactory: BuildEvaluationFactory(),
            repository: services.GetRequiredService<IRepository<IEvaluator>>());

        var result = await concrete.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Excellent);
    }

    [TestMethod]
    public async Task JsonSchemaMatch_InvalidResponse_ScoresTerrible()
    {
        IServiceProvider services = GetServices();
        var schema = """{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}""";
        var evaluator = new JsonSchemaMatchEvaluator(
            schema,
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        var testResult = BuildResult(actual: """{"age":5}""");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
    }

    [TestMethod]
    public async Task JsonSchemaMatch_NonJsonResponse_ScoresTerrible()
    {
        IServiceProvider services = GetServices();
        var evaluator = new JsonSchemaMatchEvaluator(
            """{"type":"object"}""",
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        var testResult = BuildResult(actual: "this is not json {");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
        result.Reasoning.Should().Contain("not valid JSON");
    }

    [TestMethod]
    public async Task NumericMatch_WithinTolerance_ScoresExcellent()
    {
        IServiceProvider services = GetServices();
        var evaluator = new NumericMatchEvaluator(
            new Regex(@"-?\d+"),
            tolerance: 1m,
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        var testResult = BuildResult(expected: "value is 100", actual: "value is 101");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Excellent);
    }

    [TestMethod]
    public async Task NumericMatch_OutsideTolerance_ScoresTerrible()
    {
        IServiceProvider services = GetServices();
        var evaluator = new NumericMatchEvaluator(
            new Regex(@"-?\d+"),
            tolerance: 1m,
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        var testResult = BuildResult(expected: "value is 100", actual: "value is 200");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
        result.Reasoning.Should().Contain("delta");
    }

    [TestMethod]
    public async Task NumericMatch_PatternMissesActual_ScoresTerrible()
    {
        IServiceProvider services = GetServices();
        var evaluator = new NumericMatchEvaluator(
            new Regex(@"-?\d+"),
            tolerance: 0.0m,
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        var testResult = BuildResult(expected: "42", actual: "no number here");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
    }

    [TestMethod]
    public async Task NumericMatch_PatternMissesExpected_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var evaluator = new NumericMatchEvaluator(
            new Regex(@"-?\d+"),
            tolerance: 0.0m,
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        var testResult = BuildResult(expected: "no number", actual: "42");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task NumericMatch_NegativeNumbersInTolerance_Passes()
    {
        IServiceProvider services = GetServices();
        var evaluator = new NumericMatchEvaluator(
            new Regex(@"-?\d+"),
            tolerance: 5m,
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        var testResult = BuildResult(expected: "result -10", actual: "result -12");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Excellent);
    }

    [TestMethod]
    public async Task ExactMatch_IdenticalText_PassesWithAcceptable()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>();
        var evaluator = await generator.CreateAsync(CancellationToken);
        var testResult = BuildResult(expected: "the answer", actual: "the answer");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Acceptable);
    }

    [TestMethod]
    public async Task ExactMatch_DifferentText_ScoresTerrible()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>();
        var evaluator = await generator.CreateAsync(CancellationToken);
        var testResult = BuildResult(expected: "the answer", actual: "the wrong answer");

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
    }

    [TestMethod]
    public async Task NumericMatch_DecimalValue_ScoresIdenticallyAcrossServerCultures()
    {
        IServiceProvider services = GetServices();
        var evaluator = new NumericMatchEvaluator(
            new Regex(@"-?\d+(?:\.\d+)?"),
            tolerance: 0.05m,
            await GetProject(services),
            BuildEvaluationFactory(),
            services.GetRequiredService<IRepository<IEvaluator>>());
        // "3.14" vs "3.15" is within the 0.05 tolerance. Parsing with the ambient culture on a
        // de-DE host would read the decimals as 314/315 (delta 1 → Terrible), making the score
        // depend on server locale; invariant-culture parsing keeps it Excellent everywhere.
        var testResult = BuildResult(expected: "3.14", actual: "3.15");

        var invariant = EvaluateUnderCulture(
            evaluator, testResult, CultureInfo.InvariantCulture, CancellationToken);
        var german = EvaluateUnderCulture(
            evaluator, testResult, CultureInfo.GetCultureInfo("de-DE"), CancellationToken);

        invariant.Should().NotBeNull();
        german.Should().NotBeNull();
        german.Score.Should().Be(invariant.Score);
        german.Score.Should().Be(EvaluationScore.Excellent);
    }

    private static IEvaluation? EvaluateUnderCulture(
        NumericMatchEvaluator evaluator,
        ITestResult testResult,
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        IEvaluation? evaluation = null;
        Exception? failure = null;

        // Run the (synchronous) evaluation on a dedicated thread whose culture we set, so the
        // ambient-culture mutation never leaks into other tests regardless of parallelization.
        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = culture;
                evaluation = evaluator
                    .EvaluateAsync(testResult, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }

        return evaluation;
    }

    private static IEvaluation.Create BuildEvaluationFactory()
        => (evaluator, score, latency, tokenUsage, cost, reasoning) =>
        {
            var e = Substitute.For<IEvaluation>();
            e.Evaluator.Returns(evaluator);
            e.Score.Returns(score);
            e.Reasoning.Returns(reasoning);
            e.Latency.Returns(latency);
            e.TokenUsage.Returns(tokenUsage);
            e.Cost.Returns(cost);
            return e;
        };

    private async Task<IProject> GetProject(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IProject>>().GetOrCreateAsync(CancellationToken);

    private static ITestResult BuildResult(string actual = "actual", string expected = "expected")
    {
        var testCase = Substitute.For<ITestCase>();
        testCase.ExpectedOutput.Returns(new AssistantMessage([Content.FromText(expected)], []));
        var result = Substitute.For<ITestResult>();
        result.ActualResponse.Returns(new AssistantMessage([Content.FromText(actual)], []));
        result.TestCase.Returns(testCase);
        return result;
    }

    /// <summary>A result whose expected/actual sides are tool calls rather than text.</summary>
    private static ITestResult BuildToolResult(
        IReadOnlyList<ToolRequest> expected,
        IReadOnlyList<ToolRequest> actual)
    {
        var testCase = Substitute.For<ITestCase>();
        testCase.ExpectedOutput.Returns(new AssistantMessage([], expected));
        var result = Substitute.For<ITestResult>();
        result.ActualResponse.Returns(new AssistantMessage([], actual));
        result.TestCase.Returns(testCase);
        return result;
    }

    [TestMethod]
    public async Task ExactMatch_MatchingToolCall_PassesRegardlessOfCallId()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>()
            .CreateAsync(CancellationToken);
        var testResult = BuildToolResult(
            expected: [new ToolRequest("local-1", "get_order", """{"order_id":"91"}""")],
            actual: [new ToolRequest("call_xyz", "get_order", """{"order_id":"91"}""")]);

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Acceptable);
    }

    [TestMethod]
    public async Task ExactMatch_WrongToolArgument_Fails()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>()
            .CreateAsync(CancellationToken);
        var testResult = BuildToolResult(
            expected: [new ToolRequest("a", "issue_refund", """{"order_id":"91"}""")],
            actual: [new ToolRequest("b", "issue_refund", """{"order_id":"92"}""")]);

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
        result.Reasoning.Should().Contain("issue_refund");
    }

    [TestMethod]
    public async Task ExactMatch_ExpectedToolCallNeverMade_Fails()
    {
        // The regression this whole change exists for: before tool requests were compared, an
        // expected tool call had zero content parts and matched ANY tool-free response.
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>()
            .CreateAsync(CancellationToken);
        var testResult = BuildToolResult(
            expected: [new ToolRequest("a", "get_order", "{}")],
            actual: []);

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
        result.Reasoning.Should().Contain("was not called");
    }

    [TestMethod]
    public async Task ExactMatch_UnexpectedToolCall_Fails()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>()
            .CreateAsync(CancellationToken);
        var testResult = BuildToolResult(expected: [], actual: [new ToolRequest("b", "delete_order", "{}")]);

        var result = await evaluator.EvaluateAsync(testResult, CancellationToken);

        result.Should().NotBeNull();
        result.Score.Should().Be(EvaluationScore.Terrible);
        result.Reasoning.Should().Contain("delete_order");
    }
}
