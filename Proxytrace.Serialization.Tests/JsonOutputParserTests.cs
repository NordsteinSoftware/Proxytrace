using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json;
using AwesomeAssertions;
using JetBrains.Annotations;
using Microsoft.Testing.Platform.Services;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Testing;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Proxytrace.Serialization.Internal;

// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Proxytrace.Serialization.Tests;

[TestClass]
public class JsonOutputParserTests : BaseTest<Module>
{
    public class SimpleModel
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }

    [UsedImplicitly]
    public class ModelWithValidation : IValidatableObject
    {
        public string RequiredField { get; set; } = string.Empty;

        [Range(1, 100)] public int Age { get; set; }

        public void Validate()
        {

        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return Validation.NotNullOrWhiteSpace(RequiredField);
            yield return Validation.InRange(Age, 1, 100);
        }
    }

    public enum TestEnum
    {
        FirstValue,
        SecondValue,
        ThirdValue
    }

    [UsedImplicitly]
    public class ModelWithEnum
    {
        public TestEnum Status { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class ComplexModel
    {
        public SimpleModel? NestedModel { get; set; }
        public List<string> Items { get; set; } = [];
        public Dictionary<string, int> Scores { get; set; } = new();
        public TestEnum Status { get; set; }
    }

    [UsedImplicitly]
    public class NullableModel
    {
        public string? OptionalString { get; set; }
        public int? OptionalInt { get; set; }
        public DateTime? OptionalDate { get; set; }
    }

    [UsedImplicitly]
    public class ModelWithDescription
    {
        [System.ComponentModel.Description("The action that was requested to be performed")]
        public string RequestedAction { get; set; } = string.Empty;

        [System.ComponentModel.Description("The unique identifier for the actor")]
        public Guid ActorId { get; set; }

        [System.ComponentModel.Description("The status of the operation")]
        public TestEnum Status { get; set; }
    }

    private IOutputFormat Format<T>() =>
        GetServices().GetRequiredService<IOutputFormat.Create>()(typeof(T));

    [TestMethod]
    public void SchemaDefinition_SimpleModel_ReturnsValidJsonSchema()
    {
        // Arrange & Act
        string schema = Format<SimpleModel>().As<JsonOutputFormat>().Schema;

        // Assert
        schema.Should().NotBeNullOrWhiteSpace();

        // Verify it's valid JSON
        JsonDocument.Parse(schema);

        // Verify schema contains expected properties
        schema.Should().Contain("Name");
        schema.Should().Contain("Age");
        schema.Should().Contain("IsActive");
    }

    [TestMethod]
    public void SchemaDefinition_ModelWithEnum_ContainsEnumValues()
    {
        // Arrange & Act
        string schema = Format<ModelWithEnum>().As<JsonOutputFormat>().Schema;

        // Assert
        schema.Should().NotBeNullOrWhiteSpace();

        // Verify schema contains enum values in camelCase
        schema.Should().Contain("firstValue");
        schema.Should().Contain("secondValue");
        schema.Should().Contain("thirdValue");
    }

    [TestMethod]
    public void SchemaDefinition_ComplexModel_ContainsNestedStructures()
    {
        // Arrange & Act
        string schema = Format<ComplexModel>().As<JsonOutputFormat>().Schema;

        // Assert
        schema.Should().NotBeNullOrWhiteSpace();
        JsonDocument.Parse(schema); // Verify valid JSON

        schema.Should().Contain("NestedModel");
        schema.Should().Contain("Items");
        schema.Should().Contain("Scores");
        schema.Should().Contain("Status");
    }

    [TestMethod]
    public void SchemaDefinition_NullableModel_HandlesNullableProperties()
    {
        // Arrange & Act
        string schema = Format<NullableModel>().As<JsonOutputFormat>().Schema;

        // Assert
        schema.Should().NotBeNullOrWhiteSpace();
        JsonDocument.Parse(schema); // Verify valid JSON

        schema.Should().Contain("OptionalString");
        schema.Should().Contain("OptionalInt");
        schema.Should().Contain("OptionalDate");
    }

    [TestMethod]
    public void SchemaDefinition_SameTypeMultipleCalls_ReturnsSameSchema()
    {
        // Arrange & Act
        string schema1 = Format<SimpleModel>().As<JsonOutputFormat>().Schema;
        string schema2 = Format<SimpleModel>().As<JsonOutputFormat>().Schema;

        // Assert
        schema1.Should().Be(schema2);
    }

    [TestMethod]
    public async Task ParseAsync_ValidSimpleJson_ReturnsDeserializedObject()
    {
        // Arrange
        string json = """{"name":"John","age":30,"isActive":true}""";

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("John");
        result.Age.Should().Be(30);
        result.IsActive.Should().BeTrue();
    }

    [TestMethod]
    public async Task ParseAsync_ValidJsonWithWhitespace_ReturnsDeserializedObject()
    {
        // Arrange
        string json = """
                      {
                          "name": "Jane",
                          "age": 25,
                          "isActive": false
                      }
                      """;

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Jane");
        result.Age.Should().Be(25);
        result.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task ParseAsync_EnumAsString_ParsesCorrectly()
    {
        // Arrange
        string json = """{"status":"firstValue","description":"Test"}""";

        // Act
        var result = await Format<ModelWithEnum>().ParseAsync<ModelWithEnum>(json);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestEnum.FirstValue);
        result.Description.Should().Be("Test");
    }

    [TestMethod]
    public async Task ParseAsync_EnumAsInteger_ParsesCorrectly()
    {
        // Arrange
        string json = """{"status":1,"description":"Test"}""";

        // Act
        var result = await Format<ModelWithEnum>().ParseAsync<ModelWithEnum>(json);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestEnum.SecondValue);
        result.Description.Should().Be("Test");
    }

    [TestMethod]
    public async Task ParseAsync_ComplexModel_ParsesCorrectly()
    {
        // Arrange
        string json = """
                      {
                          "nestedModel": {"name":"Nested","age":20,"isActive":true},
                          "items": ["item1","item2","item3"],
                          "scores": {"math":95,"science":87},
                          "status": "secondValue"
                      }
                      """;

        // Act
        var result = await Format<ComplexModel>().ParseAsync<ComplexModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.NestedModel.Should().NotBeNull();
        result.NestedModel.Name.Should().Be("Nested");
        result.NestedModel.Age.Should().Be(20);
        result.NestedModel.IsActive.Should().BeTrue();
        result.Items.Should().BeEquivalentTo("item1", "item2", "item3");
        result.Scores.Should().ContainKey("math").WhoseValue.Should().Be(95);
        result.Scores.Should().ContainKey("science").WhoseValue.Should().Be(87);
        result.Status.Should().Be(TestEnum.SecondValue);
    }

    [TestMethod]
    public async Task ParseAsync_NullableProperties_ParsesCorrectly()
    {
        // Arrange
        string json = """{"optionalString":"test","optionalInt":42,"optionalDate":"2023-01-01T00:00:00Z"}""";

        // Act
        var result = await Format<NullableModel>().ParseAsync<NullableModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.OptionalString.Should().Be("test");
        result.OptionalInt.Should().Be(42);
        result.OptionalDate.Should().Be(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("null")]
    [DataRow("\"null\"")]
    [DataRow("\"null\"\r\n")]
    public async Task ParseAsync_NullableListWithNull_ReturnNull(string? data)
    {
        var result = await Format<IReadOnlyList<string>>().ParseAsync<IReadOnlyList<string>>(data);
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ParseAsync_NullablePropertiesWithNulls_ParsesCorrectly()
    {
        // Arrange
        string json = """{"optionalString":null,"optionalInt":null,"optionalDate":null}""";

        // Act
        var result = await Format<NullableModel>().ParseAsync<NullableModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.OptionalString.Should().BeNull();
        result.OptionalInt.Should().BeNull();
        result.OptionalDate.Should().BeNull();
    }

    [TestMethod]
    public async Task ParseAsync_ValidModelWithValidation_PassesValidation()
    {
        // Arrange
        string json = """{"requiredField":"Valid Value","age":25}""";

        // Act
        var result = await Format<ModelWithValidation>().ParseAsync<ModelWithValidation>(json);

        // Assert
        result.Should().NotBeNull();
        result.RequiredField.Should().Be("Valid Value");
        result.Age.Should().Be(25);
    }

    [TestMethod]
    public async Task ParseAsync_JsonWithCodeBlockMarkdown_TrimsArtifacts()
    {
        // Arrange
        string jsonWithArtifacts = """
                                   ```json
                                   {"name":"John","age":30,"isActive":true}
                                   ```
                                   """;

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(jsonWithArtifacts);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("John");
        result.Age.Should().Be(30);
        result.IsActive.Should().BeTrue();
    }

    [TestMethod]
    public async Task ParseAsync_JsonWithLanguageSpecifier_TrimsArtifacts()
    {
        // Arrange
        string jsonWithArtifacts = """
                                   ```csharp
                                   {"name":"John","age":30,"isActive":true}
                                   ```
                                   """;

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(jsonWithArtifacts);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("John");
    }

    [TestMethod]
    public async Task ParseAsync_JsonWithCommonPrefixes_TrimsArtifacts()
    {
        var testCases = new[]
        {
            """json: {"name":"John","age":30,"isActive":true}""",
            """Output: {"name":"John","age":30,"isActive":true}""",
            """Result: {"name":"John","age":30,"isActive":true}""",
            """Response: {"name":"John","age":30,"isActive":true}"""
        };

        foreach (string testCase in testCases)
        {
            // Act
            var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(testCase);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be("John");
            result.Age.Should().Be(30);
            result.IsActive.Should().BeTrue();
        }
    }

    [TestMethod]
    [DataRow("28c50307-b609-4c23-ae7d-c5e51f636a82")]
    public async Task ParseAsync_Guid_works(string input)
    {
        // Act
        Guid? result = await Format<Guid>().ParseAsync<Guid>(input);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(Guid.Parse(input));
    }

    [TestMethod]
    public async Task ParseAsync_JsonWithOnlyOpeningBackticks_TrimsArtifacts()
    {
        // Arrange
        string jsonWithArtifacts = """```{"name":"John","age":30,"isActive":true}""";

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(jsonWithArtifacts);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("John");
    }

    [TestMethod]
    public async Task ParseAsync_JsonWithOnlyClosingBackticks_TrimsArtifacts()
    {
        // Arrange
        string jsonWithArtifacts = """{"name":"John","age":30,"isActive":true}```""";

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(jsonWithArtifacts);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("John");
    }

    [TestMethod]
    public async Task ParseAsync_EmptyString_ReturnsNull()
    {
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(string.Empty);
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ParseAsync_WhitespaceOnly_ReturnsEmptyAfterTrimming()
    {
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>("   \n\t   ");
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ParseAsync_InvalidJson_ThrowsSerializationException()
    {
        // Structurally balanced but meaningless — nothing suggests the model was cut off, so there
        // is nothing to recover and the failure must surface.
        string invalidJson = """{"name":"John","age":}""";

        // Act & Assert
        await Format<SimpleModel>().Invoking(f => f.ParseAsync<SimpleModel>(invalidJson))
            .Should()
            .ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task ParseAsync_JsonReturningNull_ReturnNull()
    {
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>("null");
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ParseAsync_ModelWithValidationErrors_ThrowsValidationException()
    {
        // Arrange
        string invalidJson = """{"requiredField":"","age":150}""";

        // Act & Assert
        await Format<ModelWithValidation>().Invoking(f => f.ParseAsync<ModelWithValidation>(invalidJson))
            .Should()
            .ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task ParseAsync_ModelWithValidationEmptyRequiredField_ThrowsValidationException()
    {
        // Arrange
        string invalidJson = """{"requiredField":"","age":25}""";

        // Act & Assert
        await Format<ModelWithValidation>().Invoking(f => f.ParseAsync<ModelWithValidation>(invalidJson))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task ParseAsync_ModelWithValidationInvalidAge_ThrowsValidationException()
    {
        // Arrange
        string invalidJson = """{"requiredField":"Valid","age":0}""";

        // Act & Assert
        await Format<ModelWithValidation>().Invoking(f => f.ParseAsync<ModelWithValidation>(invalidJson))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task ParseAsync_IncorrectJsonStructure_ThrowsSerializationException()
    {
        // Arrange
        string wrongStructure = """["array","instead","of","object"]""";

        // Act & Assert
        await Format<SimpleModel>().Invoking(f => f.ParseAsync<SimpleModel>(wrongStructure))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task ParseAsync_InvalidEnumValue_ThrowsSerializationException()
    {
        // Arrange
        string invalidEnum = """{"status":"invalidValue","description":"Test"}""";

        // Act & Assert
        await Format<ModelWithEnum>().Invoking(f => f.ParseAsync<ModelWithEnum>(invalidEnum))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public void SchemaDefinition_ModelWithDescription_ContainsDescriptions()
    {
        // Arrange & Act
        string schema = Format<ModelWithDescription>().As<JsonOutputFormat>().Schema;

        // Assert
        schema.Should().NotBeNullOrWhiteSpace();

        // Verify it's valid JSON
        JsonDocument.Parse(schema);

        // Verify schema contains description attributes
        schema.Should().Contain("The action that was requested to be performed");
        schema.Should().Contain("The unique identifier for the actor");
        schema.Should().Contain("The status of the operation");

        // Verify the properties exist
        schema.Should().Contain("RequestedAction");
        schema.Should().Contain("ActorId");
        schema.Should().Contain("Status");
    }

    [TestMethod]
    public async Task ParseAsync_ModelWithDescription_SuccessfullyParsed()
    {
        // Arrange
        Guid testGuid = Guid.NewGuid();
        string json = $$"""{"requestedAction":"TestAction","actorId":"{{testGuid}}","status":"secondValue"}""";

        // Act
        var result = await Format<ModelWithDescription>().ParseAsync<ModelWithDescription>(json);

        // Assert
        result.Should().NotBeNull();
        result.RequestedAction.Should().Be("TestAction");
        result.ActorId.Should().Be(testGuid);
        result.Status.Should().Be(TestEnum.SecondValue);
    }

    [TestMethod]
    public async Task ParseAsync_WithCancellationToken_RespectsToken()
    {
        // Arrange
        string json = """{"name":"John","age":30,"isActive":true}""";
        using var cts = new CancellationTokenSource();

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(json, cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("John");
    }

    [TestMethod]
    public async Task ParseAsync_WithCancelledToken_ThrowsOperationCancelledException()
    {
        // Arrange
        string json = """{"name":"John","age":30,"isActive":true}""";
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Format<SimpleModel>().Invoking(f => f.ParseAsync<SimpleModel>(json, cts.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task ParseAsync_LargeJson_ParsesSuccessfully()
    {
        // Arrange
        var items = Enumerable.Range(1, 1000).Select(i => $"item{i}").ToList();
        var scores = Enumerable.Range(1, 100).ToDictionary(i => $"subject{i}", i => i * 10);

        var model = new ComplexModel
        {
            NestedModel = new SimpleModel { Name = "Large Test", Age = 50, IsActive = true },
            Items = items,
            Scores = scores,
            Status = TestEnum.ThirdValue
        };

        string json = JsonSerializer.Serialize(model);

        // Act
        var result = await Format<ComplexModel>().ParseAsync<ComplexModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1000);
        result.Scores.Should().HaveCount(100);
        result.NestedModel.Should().NotBeNull();
        result.Status.Should().Be(TestEnum.ThirdValue);
    }

    [TestMethod]
    public async Task ParseAsync_UnicodeCharacters_ParsesCorrectly()
    {
        // Arrange
        string json = """{"name":"João 你好 🌟","age":30,"isActive":true}""";

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("João 你好 🌟");
        result.Age.Should().Be(30);
        result.IsActive.Should().BeTrue();
    }

    [TestMethod]
    public async Task ParseAsync_EscapedCharacters_ParsesCorrectly()
    {
        // Arrange
        string json = """{"name":"Line1\nLine2\tTabbed\"Quoted\"","age":30,"isActive":true}""";

        // Act
        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(json);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Line1\nLine2\tTabbed\"Quoted\"");
    }

    [TestMethod]
    public void GetFormat_MultipleTypesAndCalls_ReturnsCorrectSchemas()
    {
        string schema1 = Format<SimpleModel>().As<JsonOutputFormat>().Schema;
        string schema2 = Format<SimpleModel>().As<JsonOutputFormat>().Schema;
        string schema3 = Format<ModelWithEnum>().As<JsonOutputFormat>().Schema;
        schema1.Should().Be(schema2);
        schema1.Should().NotBe(schema3);
    }

    // ── truncated model output ────────────────────────────────────────────────
    //
    // A judge that talks past its output budget stops mid-token: the JSON arrives with an
    // unterminated string and unclosed braces. Everything before the cut is intact, and the field
    // that carries the answer is usually written before the long prose that ran out of room — so the
    // answer is recovered rather than discarded with its tail.

    [TestMethod]
    public async Task ParseAsync_CutOffMidString_RecoversTheFieldsWrittenBeforeTheCut()
    {
        // Verbatim shape of the real truncation from issue #453: a complete verdict followed by a
        // reasoning string that simply stops — no closing quote, no closing brace.
        string json = """{"status":"firstValue","description":"The response clearly addresses the""";

        var result = await Format<ModelWithEnum>().ParseAsync<ModelWithEnum>(json);

        result.Should().NotBeNull();
        result.Status.Should().Be(TestEnum.FirstValue);
        result.Description.Should().Be("The response clearly addresses the");
    }

    [TestMethod]
    public async Task ParseAsync_CutOffMidPropertyName_DropsTheIncompleteMemberAndKeepsTheRest()
    {
        // Closing the open brace cannot rescue a half-written key, so the incomplete member is
        // dropped and the members that did complete are kept.
        string json = """{"name":"John","age":30,"isAct""";

        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(json);

        result.Should().NotBeNull();
        result.Name.Should().Be("John");
        result.Age.Should().Be(30);
        result.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task ParseAsync_CutOffInsideNestedObject_ClosesEveryOpenContainer()
    {
        string json = """{"items":["a","b"],"nestedModel":{"name":"Jane","age":25""";

        var result = await Format<ComplexModel>().ParseAsync<ComplexModel>(json);

        result.Should().NotBeNull();
        result.Items.Should().Equal("a", "b");
        result.NestedModel.Should().NotBeNull();
        result.NestedModel.Name.Should().Be("Jane");
    }

    [TestMethod]
    public async Task ParseAsync_CutOffAfterAPropertyColon_KeepsTheCompletedMembers()
    {
        string json = """{"name":"John","age":30,"isActive":""";

        var result = await Format<SimpleModel>().ParseAsync<SimpleModel>(json);

        result.Should().NotBeNull();
        result.Name.Should().Be("John");
        result.Age.Should().Be(30);
    }

    [TestMethod]
    public async Task ParseAsync_MalformedBeyondTruncation_StillThrows()
    {
        // Repair is only for output that was cut off. Genuinely broken JSON must not be papered
        // over into some object that the model never said.
        string json = """{"name" "John" 30 true}""";

        await FluentActions
            .Invoking(() => Format<SimpleModel>().ParseAsync<SimpleModel>(json))
            .Should().ThrowAsync<SerializationException>();
    }

    [TestMethod]
    public async Task SerializeDictionaryArguments()
    {
        string json = """{"query":"DFS algorithm"}""";
        var serializer = GetServices().GetRequiredService<ISerializer>();
        var expected = new Dictionary<string, object?> { { "query", "DFS algorithm" } };
        var actual = await serializer.DeserializeAsync<Dictionary<string, object?>>(json);
        actual.Should().BeEquivalentTo(expected);
    }
}
