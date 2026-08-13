using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
using AwesomeAssertions;
using Proxytrace.Serialization.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Serialization.Tests;

[TestClass]
public sealed class ObjectToInferredTypesConverterTests : BaseTest<Module>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ObjectToInferredTypesConverter() },
    };

    private static object? Read(string json) =>
        JsonSerializer.Deserialize<object>(json, Options);

    [TestMethod]
    public void Read_TrueLiteral_ReturnsBool()
        => Read("true").Should().Be(true);

    [TestMethod]
    public void Read_FalseLiteral_ReturnsBool()
        => Read("false").Should().Be(false);

    [TestMethod]
    public void Read_NullLiteral_ReturnsNull()
        => Read("null").Should().BeNull();

    [TestMethod]
    public void Read_Integer_ReturnsInt()
    {
        var result = Read("42");
        result.Should().BeOfType<int>().And.Be(42);
    }

    [TestMethod]
    public void Read_LargeNumber_ReturnsLong()
    {
        long value = (long)int.MaxValue + 1;
        var result = Read(value.ToString());
        result.Should().BeOfType<long>().And.Be(value);
    }

    [TestMethod]
    public void Read_Decimal_ReturnsDouble()
    {
        var result = Read("3.14");
        result.Should().BeOfType<double>().And.Be(3.14);
    }

    [TestMethod]
    public void Read_DateTimeString_ReturnsDateTime()
    {
        var result = Read("\"2026-05-13T12:00:00Z\"");
        result.Should().BeOfType<DateTime>();
    }

    [TestMethod]
    public void Read_GuidString_ReturnsGuid()
    {
        var id = Guid.NewGuid();
        var result = Read($"\"{id}\"");
        result.Should().BeOfType<Guid>().And.Be(id);
    }

    [TestMethod]
    public void Read_PlainString_ReturnsString()
    {
        var result = Read("\"hello\"");
        result.Should().BeOfType<string>().And.Be("hello");
    }

    [TestMethod]
    public void Read_Object_ReturnsDictionary()
    {
        var result = Read("""{"name":"alice","age":30,"active":true}""");

        var dict = result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict["name"].Should().Be("alice");
        dict["age"].Should().Be(30);
        dict["active"].Should().Be(true);
    }

    [TestMethod]
    public void Read_Array_ReturnsList()
    {
        var result = Read("""[1, "two", true, null]""");

        var list = result.Should().BeOfType<List<object?>>().Subject;
        list.Should().HaveCount(4);
        list[0].Should().Be(1);
        list[1].Should().Be("two");
        list[2].Should().Be(true);
        list[3].Should().BeNull();
    }

    [TestMethod]
    public void Read_NestedObject_RecursesIntoDictionaries()
    {
        var result = Read("""{"outer":{"inner":7}}""");
        var outer = result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        var inner = outer["outer"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        inner["inner"].Should().Be(7);
    }

    [TestMethod]
    public void Read_NestedArray_RecursesIntoLists()
    {
        var result = Read("""[[1,2],[3]]""");
        var outer = result.Should().BeOfType<List<object?>>().Subject;
        outer[0].Should().BeOfType<List<object?>>().Subject.Should().HaveCount(2);
        outer[1].Should().BeOfType<List<object?>>().Subject.Should().HaveCount(1);
    }

    [TestMethod]
    public void Write_SerializesUnderlyingType()
    {
        var dict = new Dictionary<string, object?> { ["k"] = 42 };
        var json = JsonSerializer.Serialize(dict, Options);
        json.Should().Contain("\"k\":42");
    }

    [TestMethod]
    public void Read_ObjectWithMissingValue_ThrowsJsonException()
    {
        var act = () => Read("""{"name"}""");
        act.Should().Throw<JsonException>();
    }
}
