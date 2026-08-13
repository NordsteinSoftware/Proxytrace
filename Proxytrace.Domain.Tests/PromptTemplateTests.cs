using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Testing.Platform.Services;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Prompt.Internal;

namespace Proxytrace.Domain.Tests;

[TestClass]
public class PromptTemplateTests : DomainTest<Module>
{
    [TestMethod]
    public void Constructor_WithValidTemplate_ShouldCreateInstance()
    {
        // Arrange
        var services = GetServices();
        var factory = services.GetRequiredService<IPromptTemplate.Create>();
        const string template = "Hello {{name}}!";

        // Act
        var promptTemplate = factory("test", template);

        // Assert
        promptTemplate.Template.Should().Be(template);
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("name");
    }

    [TestMethod]
    public void Variables_WithNoVariables_ShouldReturnEmptySet()
    {
        // Arrange
        const string template = "Hello world!";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().BeEmpty();
    }

    [TestMethod]
    public void Variables_WithOldTemplateVariable_ShouldReturnEmptySet()
    {
        // Arrange
        const string template = "Hello {name}!";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().BeEmpty();
    }

    [TestMethod]
    public void Variables_WithSingleVariable_ShouldExtractVariable()
    {
        // Arrange
        const string template = "Hello {{name}}!";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("name");
    }

    [TestMethod]
    public void Variables_WithMultipleVariables_ShouldExtractAllVariables()
    {
        // Arrange
        const string template = "Hello {{firstName}} {{lastName}}! You are {{age}} years old.";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().HaveCount(3);
        promptTemplate.Variables.Should().Contain("firstName");
        promptTemplate.Variables.Should().Contain("lastName");
        promptTemplate.Variables.Should().Contain("age");
    }

    [TestMethod]
    public void Variables_WithDuplicateVariables_ShouldReturnUniqueVariables()
    {
        // Arrange
        const string template = "Hello {{name}}! Nice to meet you, {{name}}.";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("name");
    }

    [TestMethod]
    public void Variables_WithValidVariableNames_ShouldExtractAllValidVariables()
    {
        // Arrange
        const string template =
            "Test {{var1}} {{var_2}} {{var-3}} {{VarCamelCase}} {{VAR_UPPER}} {{var123}} {{_underscore}} {{-hyphen}}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().HaveCount(8);
        promptTemplate.Variables.Should().Contain("var1");
        promptTemplate.Variables.Should().Contain("var_2");
        promptTemplate.Variables.Should().Contain("var-3");
        promptTemplate.Variables.Should().Contain("VarCamelCase");
        promptTemplate.Variables.Should().Contain("VAR_UPPER");
        promptTemplate.Variables.Should().Contain("var123");
        promptTemplate.Variables.Should().Contain("_underscore");
        promptTemplate.Variables.Should().Contain("-hyphen");
    }

    [TestMethod]
    public void Variables_WithInvalidVariableNames_ShouldIgnoreInvalidVariables()
    {
        // Arrange
        const string template =
            "Valid: {{validVar}} Invalid: {{var with space}} {{var@symbol}} {{var.dot}} {{var!exclamation}} {{}}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("validVar");
    }

    [TestMethod]
    [DataRow("""{{"value"}}""")]
    [DataRow("""{{"key":"value"}}""")]
    [DataRow("""
             {{
                "key":"value"
             }}
             """)]
    public void Variables_WithJsonString_ShouldNotContainVariables(string template)
    {
        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().BeEmpty();
    }

    [TestMethod]
    public void Variables_WithWhitespaceInBraces_ShouldIgnoreWhitespaceVariables()
    {
        // Arrange
        const string template = "Valid: {{name}} Invalid: {{ name }} {{name }} {{ name}} {{first name}} {{last  name}}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("name");
    }

    [TestMethod]
    public void Variables_WithSpecialCharactersInBraces_ShouldIgnoreInvalidVariables()
    {
        // Arrange
        const string template = "Test {{valid}} {{in@valid}} {{in.valid}} {{in!valid}} {{in valid}} {{in,valid}} {{in;valid}}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("valid");
    }

    [TestMethod]
    public void Variables_WithNestedBraces_ShouldHandleCorrectly()
    {
        // Arrange
        const string template = "Test {{outer{{inner}}}}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("inner");
    }

    [TestMethod]
    public void Variables_WithEmptyBraces_ShouldIgnoreEmptyBraces()
    {
        // Arrange
        const string template = "Test {{}} {{valid}} {{ }}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("valid");
    }

    [TestMethod]
    public void Render_WithNoVariables_ShouldReturnOriginalTemplate()
    {
        // Arrange
        const string template = "Hello world!";
        var promptTemplate = new PromptTemplate("test", template);

        // Act
        var result = promptTemplate.Render(new Dictionary<string, string>());

        // Assert
        result.Should().NotBeNull();
        result.ToPromptString().Should().Be(template);
        result.Name.Should().Be(promptTemplate.Name);
    }

    [TestMethod]
    public void Render_WithSingleVariable_ShouldReplaceVariable()
    {
        // Arrange
        const string template = "Hello {{name}}!";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["name"] = "John" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello John!");
    }

    [TestMethod]
    public void Render_WithMultipleVariables_ShouldReplaceAllVariables()
    {
        // Arrange
        const string template = "Hello {{firstName}} {{lastName}}! You are {{age}} years old.";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string>
        {
            ["firstName"] = "John",
            ["lastName"] = "Doe",
            ["age"] = "30"
        };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello John Doe! You are 30 years old.");
    }

    [TestMethod]
    public void Render_WithDuplicateVariables_ShouldReplaceAllOccurrences()
    {
        // Arrange
        const string template = "Hello {{name}}! Nice to meet you, {{name}}.";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["name"] = "Alice" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello Alice! Nice to meet you, Alice.");
    }

    [TestMethod]
    public void Render_WithExtraValues_ShouldSucceed()
    {
        // Arrange
        const string template = "Hello {{name}}!";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string>
        {
            ["name"] = "John",
            ["extra"] = "value"
        };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello John!");
    }

    [TestMethod]
    public void Render_WithMissingVariable_ShouldThrowArgumentException()
    {
        // Arrange
        const string template = "Hello {{name}}!";
        var promptTemplate = new PromptTemplate("test", template);

        // Act & Assert
        FluentActions.Invoking(() => promptTemplate.Render(new Dictionary<string, string>()))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*Value for variable 'name' was not provided.*");
    }

    [TestMethod]
    public void Render_WithPartiallyMissingVariables_ShouldThrowArgumentException()
    {
        // Arrange
        const string template = "Hello {{firstName}} {{lastName}}!";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["firstName"] = "John" };

        // Act & Assert
        FluentActions.Invoking(() => promptTemplate.Render(values))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*Value for variable 'lastName' was not provided*");
    }

    [TestMethod]
    public void Render_WithEmptyStringValue_ShouldReplaceWithEmptyString()
    {
        // Arrange
        const string template = "Hello {{name}}!";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["name"] = "" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello !");
    }

    [TestMethod]
    public void Render_WithSpecialCharactersInValue_ShouldHandleCorrectly()
    {
        // Arrange
        const string template = "Test {{value}}";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["value"] = "special@#$%^&*()chars" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Test special@#$%^&*()chars");
    }

    [TestMethod]
    public void Render_WithBracesInValue_ShouldHandleCorrectly()
    {
        // Arrange
        const string template = "Test {{value}}";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["value"] = "{braces} in value" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Test {braces} in value");
    }

    [TestMethod]
    public void PromptTemplate_WithComplexTemplate_ShouldWorkCorrectly()
    {
        // Arrange
        const string template = """
                                You are a {{role}} assistant.
                                User: {{userInput}}
                                Context: {{context}}
                                Please provide a {{responseType}} response.
                                Remember to be {{tone}} and {{helpful}}.
                                """;
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string>
        {
            ["role"] = "helpful AI",
            ["userInput"] = "What is the weather?",
            ["context"] = "Weather inquiry",
            ["responseType"] = "detailed",
            ["tone"] = "friendly",
            ["helpful"] = "informative"
        };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        var actual = result.ToPromptString();
        var expected = """
                       You are a helpful AI assistant.
                       User: What is the weather?
                       Context: Weather inquiry
                       Please provide a detailed response.
                       Remember to be friendly and informative.
                       """;
        actual.Should().Be(expected);
        promptTemplate.Variables.Should().HaveCount(6);
    }

    [TestMethod]
    public void PromptTemplate_WithUnicodeCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        const string template = "Hello {{name}}! 你好 {{greeting}} 🎉";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string>
        {
            ["name"] = "世界",
            ["greeting"] = "مرحبا"
        };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello 世界! 你好 مرحبا 🎉");
    }

    [TestMethod]
    public void PromptTemplate_WithLongTemplate_ShouldHandleCorrectly()
    {
        // Arrange
        var template = string.Join(" ", Enumerable.Repeat("Word {{var}}", 1000));
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["var"] = "test" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Contain("Word test");
        promptTemplate.Variables.Should().ContainSingle().Which.Should().Be("var");
    }

    [TestMethod]
    public void PromptTemplate_WithVariableAtTemplateStart_ShouldWorkCorrectly()
    {
        // Arrange
        const string template = "{{greeting}} world!";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["greeting"] = "Hello" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello world!");
    }

    [TestMethod]
    public void PromptTemplate_WithVariableAtTemplateEnd_ShouldWorkCorrectly()
    {
        // Arrange
        const string template = "Hello {{name}}";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["name"] = "world!" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Hello world!");
    }

    [TestMethod]
    public void PromptTemplate_WithOnlyVariable_ShouldWorkCorrectly()
    {
        // Arrange
        const string template = "{{content}}";
        var promptTemplate = new PromptTemplate("test", template);
        var values = new Dictionary<string, string> { ["content"] = "Complete content" };

        // Act
        var result = promptTemplate.Render(values);

        // Assert
        result.ToPromptString().Should().Be("Complete content");
    }

    [TestMethod]
    public void Variables_WithNumbersOnly_ShouldThrow()
    {
        // Arrange
        const string template = "Test {{123}} {{valid}}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        // validation should fail — a purely numeric variable name has no letters
        FluentActions.Invoking(() => promptTemplate.Validate())
            .Should()
            .Throw<ValidationException>()
            .WithMessage("*must be greater than 0*");
    }

    [TestMethod]
    public void Variables_WithSingleLetter_ShouldBeValid()
    {
        // Arrange — a one-letter variable name is legal and must not be rejected.
        const string template = "Value: {{x}}";
        var promptTemplate = new PromptTemplate("test", template);

        // Act
        var rendered = promptTemplate.Render(new Dictionary<string, string> { ["x"] = "42" });

        // Assert
        rendered.ToPromptString().Should().Be("Value: 42");
    }

    [TestMethod]
    public void Variables_WithUnderscoreAndHyphenCombinations_ShouldBeValid()
    {
        // Arrange
        const string template = "Test {{var_-name}} {{-_var}} {{__var__}} {{--var--}}";

        // Act
        var promptTemplate = new PromptTemplate("test", template);

        // Assert
        promptTemplate.Variables.Should().HaveCount(4);
        promptTemplate.Variables.Should().Contain("var_-name");
        promptTemplate.Variables.Should().Contain("-_var");
        promptTemplate.Variables.Should().Contain("__var__");
        promptTemplate.Variables.Should().Contain("--var--");
    }

    [TestMethod]
    public void Equals_SeparatelyConstructedIdenticalTemplates_AreEqual()
    {
        // Regression: the synthesized record equality compared the Variables HashSet by
        // reference, so two identically-constructed templates were never equal — which silently
        // disabled the "has the prompt actually changed?" dedup in Agent.ChangeSystemMessage.
        var a = new PromptTemplate("test", "Hello {{name}}!");
        var b = new PromptTemplate("test", "Hello {{name}}!");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentTemplateText_AreNotEqual()
    {
        var a = new PromptTemplate("test", "Hello {{name}}!");
        var b = new PromptTemplate("test", "Goodbye {{name}}!");

        a.Should().NotBe(b);
    }
}