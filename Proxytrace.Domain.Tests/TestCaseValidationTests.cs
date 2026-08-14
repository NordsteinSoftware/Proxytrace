using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class TestCaseValidationTests : BaseTest<Module>
{
    [TestMethod]
    public void CreateNew_WithValidInputs_CreatesTestCase()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateNew>();
        var input = Conversation.Create();
        var expectedOutput = new AssistantMessage([Content.FromText("Hello")], []);

        // Act
        var testCase = factory(input, expectedOutput, sourceAgentCallId: null);

        // Assert
        testCase.Should().NotBeNull();
        testCase.Input.Should().Be(input);
        testCase.ExpectedOutput.Should().Be(expectedOutput);
        testCase.Id.Should().NotBe(Guid.Empty);
        testCase.CreatedAt.Should().NotBe(default);
        testCase.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public void CreateNew_WithNullInput_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateNew>();
        var expectedOutput = new AssistantMessage([Content.FromText("Hello")], []);

        // Act & Assert
        var action = () => factory.DynamicInvoke(null, expectedOutput, null);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public void CreateNew_WithNullExpectedOutput_ThrowsValidationException()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateNew>();
        var input = Conversation.Create();

        // Act & Assert
        var action = () => factory.DynamicInvoke(input, null, null);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateExisting_WithValidData_CreatesTestCase()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ITestCase.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var existing = await generator.CreateAsync(CancellationToken);

        // Act
        var testCase = createExisting(existing.Input, existing.ExpectedOutput, existing.SourceAgentCallId, existing);

        // Assert
        testCase.Should().NotBeNull();
        testCase.Id.Should().Be(existing.Id);
        testCase.Input.Should().Be(existing.Input);
        testCase.ExpectedOutput.Should().Be(existing.ExpectedOutput);
        testCase.CreatedAt.Should().Be(existing.CreatedAt);
        testCase.UpdatedAt.Should().Be(existing.UpdatedAt);
    }

    [TestMethod]
    public void CreateNew_HasNoSourceAgentCall()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateNew>();

        var testCase = factory(Conversation.Create(), new AssistantMessage([Content.FromText("Hi")], []), null);

        // A synthetic case (raw input + expected output) has no source trace.
        testCase.SourceAgentCallId.Should().BeNull();
    }

    [TestMethod]
    public async Task CreateNewFromCall_CapturesSourceAgentCallId()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateNewFromCall>();
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);

        var testCase = factory(call);

        // Promoting a trace as-is preserves the link back to the source call, and the expected output is
        // the response the agent recorded.
        var response = call.Response ?? throw new InvalidOperationException("generated call must have a response");
        testCase.SourceAgentCallId.Should().Be(call.Id);
        testCase.ExpectedOutput.Should().Be(response.Response);
    }

    [TestMethod]
    public async Task CreateCorrection_UsesCorrectedOutputAndKeepsSourceLink()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateCorrection>();
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);
        var correction = new AssistantMessage([Content.FromText("The corrected answer")], []);

        var testCase = factory(call, correction);

        // A correction keeps the call's request as input but replaces the expected output, while still
        // pointing back at the trace it corrects.
        var response = call.Response ?? throw new InvalidOperationException("generated call must have a response");
        testCase.SourceAgentCallId.Should().Be(call.Id);
        testCase.ExpectedOutput.Should().Be(correction);
        testCase.ExpectedOutput.Should().NotBe(response.Response);
    }

    [TestMethod]
    public async Task CreateExisting_RoundTripsSourceAgentCallId()
    {
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ITestCase.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var existing = await generator.CreateAsync(CancellationToken);
        var sourceId = Guid.NewGuid();

        var testCase = createExisting(existing.Input, existing.ExpectedOutput, sourceId, existing);

        testCase.SourceAgentCallId.Should().Be(sourceId);
    }

    [TestMethod]
    public void Id_IsUniqueForEachNewTestCase()
    {
        // Arrange
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateNew>();
        var input = Conversation.Create();
        var expectedOutput = new AssistantMessage([Content.FromText("Hello")], []);

        // Act
        var testCase1 = factory(input, expectedOutput, sourceAgentCallId: null);
        var testCase2 = factory(input, expectedOutput, sourceAgentCallId: null);

        // Assert
        testCase1.Id.Should().NotBe(testCase2.Id);
    }
}
