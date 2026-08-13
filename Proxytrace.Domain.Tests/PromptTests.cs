using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.Testing;

// ReSharper disable NullableWarningSuppressionIsUsed

namespace Proxytrace.Domain.Tests;

[TestClass]
public class PromptTests : BaseTest<Module>
{
    private IPrompt CreatePrompt(string name, string content, IServiceProvider services)
    {
        var factory = services.GetRequiredService<IPromptTemplate.Create>();
        var template = factory(name, content);
        return template.Render();
    }
    
    [TestMethod]
    public void Append_ShouldCombineNamesWithUnderscore()
    {
        // Arrange
        var services = GetServices();
        var prompt1 = CreatePrompt("FirstPrompt", "First content", services);
        var prompt2 = CreatePrompt("SecondPrompt", "Second content", services);

        // Act
        IPrompt result = prompt1.Append(prompt2);

        // Assert
        result.Name.Should().Be("FirstPrompt_SecondPrompt");
    }

    [TestMethod]
    public void Append_ShouldCombinePromptStringsWithNewLine()
    {
        // Arrange
        var services = GetServices();
        var prompt1 = CreatePrompt("Prompt1", "First content", services);
        var prompt2 = CreatePrompt("Prompt2", "Second content", services);

        // Act
        IPrompt result = prompt1.Append(prompt2);

        // Assert
        string expected = string.Join(Environment.NewLine, "First content", "Second content");
        result.ToPromptString().Should().Be(expected);
    }

    [TestMethod]
    public void Append_ShouldReturnNewInstance()
    {
        // Arrange
        var services = GetServices();
        var prompt1 = CreatePrompt("Prompt1", "Content1", services);
        var prompt2 = CreatePrompt("Prompt2", "Content2", services);

        // Act
        IPrompt result = prompt1.Append(prompt2);

        // Assert
        result.Should().NotBeSameAs(prompt1);
        result.Should().NotBeSameAs(prompt2);
    }

    [TestMethod]
    public void Append_ShouldNotModifyOriginalPrompts()
    {
        // Arrange
        var services = GetServices();
        var prompt1 = CreatePrompt("Original1", "Content1", services);
        var prompt2 = CreatePrompt("Original2", "Content2", services);

        // Act
        _ = prompt1.Append(prompt2);

        // Assert
        prompt1.Name.Should().Be("Original1");
        prompt1.ToPromptString().Should().Be("Content1");
        prompt2.Name.Should().Be("Original2");
        prompt2.ToPromptString().Should().Be("Content2");
    }

    [TestMethod]
    public void Append_WithMultipleAppends_ShouldChainCorrectly()
    {
        // Arrange
        var services = GetServices();
        var prompt1 = CreatePrompt("First", "Content1", services);
        var prompt2 = CreatePrompt("Second", "Content2", services);
        var prompt3 = CreatePrompt("Third", "Content3", services);

        // Act
        IPrompt result = prompt1.Append(prompt2).Append(prompt3);

        // Assert
        result.Name.Should().Be("First_Second_Third");
        string expectedContent = string.Join(Environment.NewLine, 
            "Content1", 
            "Content2", 
            "Content3");
        result.ToPromptString().Should().Be(expectedContent);
    }

    [TestMethod]
    public void Append_WithMultiLineContent_ShouldPreserveFormatting()
    {
        // Arrange
        var services = GetServices();
        var prompt1 = CreatePrompt("Prompt1", "Line1\nLine2", services);
        var prompt2 = CreatePrompt("Prompt2", "Line3\nLine4", services);

        // Act
        IPrompt result = prompt1.Append(prompt2);

        // Assert
        result.ToPromptString().Should().Contain("Line1\nLine2");
        result.ToPromptString().Should().Contain("Line3\nLine4");
    }

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreatePrompt()
    {
        // Arrange & Act
        var services = GetServices();
        var prompt = CreatePrompt("TestName", "Test content", services);

        // Assert
        prompt.Should().NotBeNull();
        prompt.Name.Should().Be("TestName");
        prompt.ToPromptString().Should().Be("Test content");
    }

    [TestMethod]
    public void Constructor_WithNullName_ShouldThrowArgumentException()
    {
        // Act & Assert
        var services = GetServices();
        var factory = services.GetRequiredService<IPromptTemplate.Create>();
        FluentActions.Invoking(() => factory.DynamicInvoke(null, "Content"))
            .Should()
            .Throw<Exception>();
    }

    [TestMethod]
    public void Constructor_WithEmptyName_ShouldThrowArgumentException()
    {
        // Act & Assert
        var services = GetServices();
        FluentActions.Invoking(() => CreatePrompt(string.Empty, "Content", services))
            .Should()
            .Throw<Exception>();
    }

    [TestMethod]
    public void Constructor_WithWhitespaceName_ShouldThrowArgumentException()
    {
        // Act & Assert
        var services = GetServices();
        FluentActions.Invoking(() => CreatePrompt("   ", "Content", services))
            .Should()
            .Throw<Exception>();
    }

    [TestMethod]
    public void Constructor_WithNullPromptString_ShouldThrowArgumentException()
    {
        // Act & Assert
        var services = GetServices();
        var factory = services.GetRequiredService<IPromptTemplate.Create>();
        FluentActions.Invoking(() => factory.DynamicInvoke("Name", null))
            .Should()
            .Throw<Exception>();
    }

    [TestMethod]
    public void Constructor_WithEmptyPromptString_ShouldThrowArgumentException()
    {
        // Act & Assert
        var services = GetServices();
        FluentActions.Invoking(() => CreatePrompt("Name", string.Empty, services))
            .Should()
            .Throw<Exception>();
    }

    [TestMethod]
    public void Constructor_WithWhitespaceName_ThrowsArgumentExceptionNotArgumentNull()
    {
        // The ctor guards non-null whitespace with ArgumentException (ArgumentNullException —
        // which derives from ArgumentException — is the wrong type for a present-but-blank value).
        var act = () => new Proxytrace.Domain.Prompt.Internal.Prompt("   ", "content");

        act.Should().Throw<ArgumentException>()
            .Which.Should().NotBeOfType<ArgumentNullException>();
    }

    [TestMethod]
    public void Constructor_WithWhitespacePromptString_ThrowsArgumentExceptionNotArgumentNull()
    {
        var act = () => new Proxytrace.Domain.Prompt.Internal.Prompt("name", "   ");

        act.Should().Throw<ArgumentException>()
            .Which.Should().NotBeOfType<ArgumentNullException>();
    }

    [TestMethod]
    public void ToPromptString_ShouldReturnOriginalContent()
    {
        // Arrange
        var services = GetServices();
        const string content = "This is the prompt content";
        var prompt = CreatePrompt("TestPrompt", content, services);

        // Act
        string result = prompt.ToPromptString();

        // Assert
        result.Should().Be(content);
    }
}