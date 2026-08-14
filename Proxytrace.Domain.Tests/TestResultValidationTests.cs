using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class TestResultValidationTests : BaseTest<Module>
{
    private ICompletion CreateCompletion(string response, IServiceProvider services)
    {
        var completionFactory = services.GetRequiredService<ICompletion.Create>();
        return completionFactory(new AssistantMessage([Content.FromText(response)], []), null, TimeSpan.FromMilliseconds(1000));
    }
    
    [TestMethod]
    public async Task CreateNew_WithValidInputs_CreatesTestResult()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestResult.CreateNew>();
        var testCase = await CreateTestCaseAsync(services);
        var completion =  CreateCompletion("Result", services);
        var testResult = factory(testCase, completion, []);

        testResult.Should().NotBeNull();
        testResult.TestCase.Should().Be(testCase);
        testResult.ActualResponse.Should().Be(completion.Response);
        testResult.Evaluations.Should().BeEmpty();
        testResult.Id.Should().NotBe(Guid.Empty);
        testResult.CreatedAt.Should().NotBe(default);
        testResult.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public Task CreateNew_WithNullTestCase_ThrowsValidationException()
    {
        try
        {
            IServiceProvider services = GetServices();
            var factory = services.GetRequiredService<ITestResult.CreateNew>();
            var completion =  CreateCompletion("Result", services);

            var action = () => factory.DynamicInvoke(null, completion, Array.Empty<IEvaluation>());
            action.Should().Throw<Exception>();
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    [TestMethod]
    public async Task CreateNew_WithNullActualResponse_ThrowsValidationException()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestResult.CreateNew>();
        var testCase = await CreateTestCaseAsync(services);

        var action = () => factory.DynamicInvoke(testCase, null, Array.Empty<IEvaluation>());
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithEvaluations_StoresEvaluations()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestResult.CreateNew>();
        var createEvaluation = services.GetRequiredService<IEvaluation.Create>();
        var evaluatorGenerator = services.GetRequiredService<IDomainEntityGenerator<Evaluator.IEvaluator>>();
        var testCase = await CreateTestCaseAsync(services);
        var completion =  CreateCompletion("Answer", services);
        
        var evaluator = await evaluatorGenerator.GetOrCreateAsync();
        var evaluation = createEvaluation(evaluator, EvaluationScore.Good, TimeSpan.FromMilliseconds(50), reasoning: "Correct");

        var testResult = factory(testCase, completion, [evaluation]);

        testResult.Evaluations.Should().ContainSingle().Which.Score.Should().Be(EvaluationScore.Good);
    }

    [TestMethod]
    public async Task CreateExisting_WithValidData_CreatesTestResult()
    {
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ITestResult.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestResult>>();
        var existing = await generator.CreateAsync(CancellationToken);

        var testResult = createExisting(
            existing.TestCase,
            existing.ActualResponse,
            existing.Evaluations,
            existing.Latency,
            existing.Usage,
            existing);

        testResult.Should().NotBeNull();
        testResult.Id.Should().Be(existing.Id);
        testResult.TestCase.Should().Be(existing.TestCase);
        testResult.ActualResponse.Should().Be(existing.ActualResponse);
        testResult.Evaluations.Should().BeEquivalentTo(existing.Evaluations);
        testResult.CreatedAt.Should().Be(existing.CreatedAt);
        testResult.UpdatedAt.Should().Be(existing.UpdatedAt);
    }

    [TestMethod]
    public async Task Id_IsUniqueForEachNewTestResult()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestResult.CreateNew>();
        var testCase = await CreateTestCaseAsync(services);
        var completion =  CreateCompletion("Result", services);

        var testResult1 = factory(testCase, completion, []);
        var testResult2 = factory(testCase, completion, []);

        testResult1.Id.Should().NotBe(testResult2.Id);
    }

    private static async Task<ITestCase> CreateTestCaseAsync(IServiceProvider services)
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        return await generator.CreateAsync();
    }
}
