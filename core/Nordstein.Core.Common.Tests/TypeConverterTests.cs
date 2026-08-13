using System.Text.Json;
using AwesomeAssertions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Conversion;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class TypeConverterTests : BaseTest<Module>
{
    private ITypeConverter Converter => GetServices().GetRequiredService<ITypeConverter>();

    [TestMethod]
    public void ChangeType_WithNull_ReturnsNull()
    {
        // Arrange
        object? nullValue = null;

        // Act
        var result = Converter.ChangeType(nullValue, typeof(string));

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void ChangeType_WithSameType_ReturnsSameValue()
    {
        // Arrange
        var value = "test string";

        // Act
        var result = Converter.ChangeType(value, typeof(string));

        // Assert
        result.Should().Be(value);
    }

    [TestMethod]
    public void ChangeType_StringToInt_Converts()
    {
        // Arrange
        var stringValue = "42";

        // Act
        var result = Converter.ChangeType(stringValue, typeof(int));

        // Assert
        result.Should().Be(42);
    }

    [TestMethod]
    public void ChangeType_StringToGuid_Converts()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var stringValue = guid.ToString();

        // Act
        var result = Converter.ChangeType(stringValue, typeof(Guid));

        // Assert
        result.Should().Be(guid);
    }

    [TestMethod]
    public void ChangeType_IntToString_Converts()
    {
        // Arrange
        var intValue = 42;

        // Act
        var result = Converter.ChangeType(intValue, typeof(string));

        // Assert
        result.Should().Be("42");
    }

    [TestMethod]
    public void ChangeType_IntToDouble_Converts()
    {
        // Arrange
        var intValue = 42;

        // Act
        var result = Converter.ChangeType(intValue, typeof(double));

        // Assert
        result.Should().Be(42.0);
    }

    [TestMethod]
    public void ChangeType_StringToNullableInt_Converts()
    {
        // Arrange
        var stringValue = "42";

        // Act
        var result = Converter.ChangeType(stringValue, typeof(int?));

        // Assert
        result.Should().Be(42);
    }

    [TestMethod]
    public void ChangeType_NullToNullableInt_ReturnsNull()
    {
        // Arrange
        object? nullValue = null;

        // Act
        var result = Converter.ChangeType(nullValue, typeof(int?));

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void ChangeType_StringToEnum_Converts()
    {
        // Arrange
        var stringValue = "Value1";

        // Act
        var result = Converter.ChangeType(stringValue, typeof(TestEnum));

        // Assert
        result.Should().Be(TestEnum.Value1);
    }

    [TestMethod]
    public void ChangeType_StringToEnumIgnoreCase_Converts()
    {
        // Arrange
        var stringValue = "value2";

        // Act
        var result = Converter.ChangeType(stringValue, typeof(TestEnum));

        // Assert
        result.Should().Be(TestEnum.Value2);
    }

    [TestMethod]
    public void ChangeType_IntToEnum_Converts()
    {
        // Arrange
        var intValue = 1;

        // Act
        var result = Converter.ChangeType(intValue, typeof(TestEnum));

        // Assert
        result.Should().Be(TestEnum.Value2);
    }

    [TestMethod]
    public void ChangeType_JsonElementInt_Converts()
    {
        // Arrange
        var json = "42";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(int));

        // Assert
        result.Should().Be(42);
    }

    [TestMethod]
    public void ChangeType_JsonElementString_Converts()
    {
        // Arrange
        var json = "\"test string\"";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(string));

        // Assert
        result.Should().Be("test string");
    }

    [TestMethod]
    public void ChangeType_JsonElementGuid_Converts()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var json = $"\"{guid}\"";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(Guid));

        // Assert
        result.Should().Be(guid);
    }

    [TestMethod]
    public void ChangeType_JsonElementBool_Converts()
    {
        // Arrange
        var json = "true";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(bool));

        // Assert
        result.Should().Be(true);
    }

    [TestMethod]
    public void ChangeType_JsonElementDouble_Converts()
    {
        // Arrange
        var json = "42.5";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(double));

        // Assert
        result.Should().Be(42.5);
    }

    [TestMethod]
    public void ChangeType_JsonElementDecimal_Converts()
    {
        // Arrange
        var json = "99.99";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(decimal));

        // Assert
        result.Should().Be(99.99m);
    }

    [TestMethod]
    public void ChangeType_JsonElementDateTime_Converts()
    {
        // Arrange
        var dateTime = DateTime.UtcNow;
        var json = $"\"{dateTime:O}\"";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        object? result = Converter.ChangeType(jsonElement, typeof(DateTime));

        // Assert
        result.Should().NotBeNull();
        ((DateTime)result).Should().BeCloseTo(dateTime, TimeSpan.FromMilliseconds(1));
    }

    [TestMethod]
    public void ChangeType_JsonElementDateTimeOffset_Converts()
    {
        // Arrange
        var dateTimeOffset = DateTimeOffset.UtcNow;
        var json = $"\"{dateTimeOffset:O}\"";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(DateTimeOffset));

        // Assert
        result.Should().NotBeNull();
        ((DateTimeOffset)result).Should().BeCloseTo(dateTimeOffset, TimeSpan.FromMilliseconds(1));
    }

    [TestMethod]
    public void ChangeType_ListOfObjectToListOfGuid_Converts()
    {
        // Arrange
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var list = new List<object> { guid1.ToString(), guid2.ToString() };

        // Act
        var result = Converter.ChangeType(list, typeof(List<Guid>));

        // Assert
        result.Should().NotBeNull();
        var guidList = (List<Guid>)result;
        guidList.Count.Should().Be(2);
        guidList.Should().Contain(guid1);
        guidList.Should().Contain(guid2);
    }

    [TestMethod]
    public void ChangeType_ListOfIntToListOfString_Converts()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act
        var result = Converter.ChangeType(list, typeof(List<string>));

        // Assert
        result.Should().NotBeNull();
        var stringList = (List<string>)result;
        stringList.Count.Should().Be(3);
        stringList.Should().Contain("1");
        stringList.Should().Contain("2");
        stringList.Should().Contain("3");
    }

    [TestMethod]
    public void ChangeType_ListToIEnumerable_Converts()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act
        var result = Converter.ChangeType(list, typeof(IEnumerable<int>));

        // Assert
        result.Should().NotBeNull();
        var enumerable = (IEnumerable<int>)result;
        enumerable.Should().BeEquivalentTo([1, 2, 3]);
    }

    [TestMethod]
    public void ChangeType_EmptyList_Converts()
    {
        // Arrange
        var list = new List<object>();

        // Act
        var result = Converter.ChangeType(list, typeof(List<int>));

        // Assert
        result.Should().NotBeNull();
        var intList = (List<int>)result;
        intList.Should().BeEmpty();
    }

    [TestMethod]
    public void ChangeType_StringToByte_Converts()
    {
        // Arrange
        var stringValue = "255";

        // Act
        var result = Converter.ChangeType(stringValue, typeof(byte));

        // Assert
        result.Should().Be((byte)255);
    }

    [TestMethod]
    public void ChangeType_StringToLong_Converts()
    {
        // Arrange
        var stringValue = "9223372036854775807";

        // Act
        var result = Converter.ChangeType(stringValue, typeof(long));

        // Assert
        result.Should().Be(9223372036854775807L);
    }

    [TestMethod]
    public void ChangeType_InvalidStringToInt_ThrowsArgumentException()
    {
        // Arrange
        var invalidString = "not a number";

        // Act & Assert
        var action = () => Converter.ChangeType(invalidString, typeof(int));
        action.Should().Throw<ArgumentException>()
            .WithMessage("Cannot convert value*");
    }

    [TestMethod]
    public void ChangeType_InvalidStringToGuid_ThrowsFormatException()
    {
        // Arrange
        var invalidString = "not a guid";

        // Act & Assert
        var action = () => Converter.ChangeType(invalidString, typeof(Guid));
        action.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void ChangeType_BoolToString_Converts()
    {
        // Arrange
        var boolValue = true;

        // Act
        var result = Converter.ChangeType(boolValue, typeof(string));

        // Assert
        result.Should().Be("True");
    }

    [TestMethod]
    public void ChangeType_StringToBool_Converts()
    {
        // Arrange
        var stringValue = "true";

        // Act
        var result = Converter.ChangeType(stringValue, typeof(bool));

        // Assert
        result.Should().Be(true);
    }

    [TestMethod]
    public void ChangeType_JsonElementEnum_Converts()
    {
        // Arrange
        var json = "\"Value1\"";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(TestEnum));

        // Assert
        result.Should().Be(TestEnum.Value1);
    }

    [TestMethod]
    public void ChangeType_IntToNullableInt_Converts()
    {
        // Arrange
        var intValue = 42;

        // Act
        var result = Converter.ChangeType(intValue, typeof(int?));

        // Assert
        result.Should().Be(42);
    }

    [TestMethod]
    public void ChangeType_WithComplexJsonElement_Deserializes()
    {
        // Arrange
        var json = "{\"Value\": 42, \"Name\": \"Test\"}";
        var jsonElement = JsonDocument.Parse(json).RootElement;

        // Act
        var result = Converter.ChangeType(jsonElement, typeof(ComplexType));

        // Assert
        result.Should().NotBeNull();
        var complexResult = (ComplexType)result;
        complexResult.Value.Should().Be(42);
        complexResult.Name.Should().Be("Test");
    }

    private enum TestEnum
    {
        Value1 = 0,
        Value2 = 1,
        // ReSharper disable once UnusedMember.Local
        Value3 = 2
    }

    [TestMethod]
    public void As_WithValidCast_ReturnsCastedObject()
    {
        // Arrange
        object obj = "test string";

        // Act
        var result = TypeConversionExtensions.As<string>(obj);

        // Assert
        result.Should().Be("test string");
    }

    [TestMethod]
    public void As_WithInvalidCast_ThrowsInvalidCastException()
    {
        // Arrange
        object obj = 42;

        // Act & Assert
        var action = () => TypeConversionExtensions.As<string>(obj);
        action.Should().Throw<InvalidCastException>()
            .WithMessage("Cannot cast object of type System.Int32 to type System.String");
    }

    [TestMethod]
    public void As_WithDerivedClass_ReturnsBaseType()
    {
        // Arrange
        object obj = new DerivedClass();

        // Act
        var result = TypeConversionExtensions.As<BaseClass>(obj);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<DerivedClass>();
    }

    private class BaseClass { }
    private class DerivedClass : BaseClass { }

    private class ComplexType
    {
        public int Value { get; [UsedImplicitly] set; }
        public string Name { get; set; } = string.Empty;
    }
}
