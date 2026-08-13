using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using JetBrains.Annotations;
using Nordstein.Core.Common.Validation;
// ReSharper disable PropertyCanBeMadeInitOnly.Local

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class ValidationTests
{
    [TestMethod]
    public void NotNullOrWhitespace_WithValidString_ReturnsSuccess()
    {
        // Arrange
        var validString = "Valid Input";

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrWhiteSpace(validString);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotNullOrWhitespace_WithNull_ReturnsError()
    {
        // Arrange
        string? nullString = null;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrWhiteSpace(nullString);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [TestMethod]
    public void NotNullOrWhitespace_WithEmptyString_ReturnsError()
    {
        // Arrange
        var emptyString = string.Empty;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrWhiteSpace(emptyString);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [TestMethod]
    public void NotNullOrWhitespace_WithWhitespaceString_ReturnsError()
    {
        // Arrange
        var whitespaceString = "   ";

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrWhiteSpace(whitespaceString);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [TestMethod]
    public void NotNullOrWhitespace_WithTabs_ReturnsError()
    {
        // Arrange
        var tabString = "\t\t\t";

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrWhiteSpace(tabString);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void NotNullOrWhitespace_WithNewlines_ReturnsError()
    {
        // Arrange
        var newlineString = "\n\r\n";

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrWhiteSpace(newlineString);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void NotNullOrEmpty_WithValidString_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrEmpty("hello");
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotNullOrEmpty_WithWhitespace_ReturnsSuccess()
    {
        // Whitespace is NOT empty — distinct from NotNullOrWhiteSpace
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrEmpty("   ");
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotNullOrEmpty_WithNull_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrEmpty(null);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be null or empty");
    }

    [TestMethod]
    public void NotNullOrEmpty_WithEmptyString_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrEmpty(string.Empty);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be null or empty");
    }

    [TestMethod]
    public void NotDefault_WithNonDefaultInt_ReturnsSuccess()
    {
        // Arrange
        var nonDefaultInt = 42;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(nonDefaultInt);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotDefault_WithDefaultInt_ReturnsError()
    {
        // Arrange
        var defaultInt = 0;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(defaultInt);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be default");
    }

    [TestMethod]
    public void NotDefault_WithNonDefaultGuid_ReturnsSuccess()
    {
        // Arrange
        var nonDefaultGuid = Guid.NewGuid();

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(nonDefaultGuid);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotDefault_WithDefaultGuid_ReturnsError()
    {
        // Arrange
        var defaultGuid = Guid.Empty;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(defaultGuid);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be default");
    }

    [TestMethod]
    public void NotDefault_WithNonDefaultDateTimeOffset_ReturnsSuccess()
    {
        // Arrange
        var nonDefaultDate = DateTimeOffset.Now;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(nonDefaultDate);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotDefault_WithDefaultDateTimeOffset_ReturnsError()
    {
        // Arrange
        var defaultDate = default(DateTimeOffset);

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(defaultDate);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void InPast_WithPastDate_ReturnsSuccess()
    {
        // Arrange
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.InPast(pastDate);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void InPast_WithFutureDate_ReturnsError()
    {
        // Arrange
        var futureDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.InPast(futureDate);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be in the past");
    }

    [TestMethod]
    public void InPast_WithCurrentMoment_ReturnsSuccess()
    {
        // Arrange
        // Current time should be considered "in the past" or at least not in the future
        var now = DateTimeOffset.UtcNow;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.InPast(now);

        // Assert
        // This might pass or fail depending on microsecond precision,
        // but generally should pass since we're not > UtcNow
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void InFuture_WithFutureDate_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.InFuture(DateTimeOffset.UtcNow.AddDays(1));
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void InFuture_WithPastDate_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.InFuture(DateTimeOffset.UtcNow.AddDays(-1));
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be in the future");
    }

    [TestMethod]
    public void InFuture_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.InFuture(DateTimeOffset.UtcNow.AddDays(-1));
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void NotBefore_WithDateAfterMinValue_ReturnsSuccess()
    {
        // Arrange
        var minValue = DateTimeOffset.UtcNow.AddDays(-10);
        var testDate = DateTimeOffset.UtcNow.AddDays(-5);

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotBefore(testDate, minValue);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotBefore_WithDateBeforeMinValue_ReturnsError()
    {
        // Arrange
        var minValue = DateTimeOffset.UtcNow.AddDays(-5);
        var testDate = DateTimeOffset.UtcNow.AddDays(-10);

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotBefore(testDate, minValue);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be before");
    }

    [TestMethod]
    public void NotBefore_WithDateEqualToMinValue_ReturnsSuccess()
    {
        // Arrange
        var minValue = DateTimeOffset.UtcNow;
        var testDate = minValue;

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotBefore(testDate, minValue);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotAfter_WithDateBeforeMaxValue_ReturnsSuccess()
    {
        var maxValue = DateTimeOffset.UtcNow.AddDays(5);
        var testDate = DateTimeOffset.UtcNow.AddDays(1);
        var result = global::Nordstein.Core.Common.Validation.Validation.NotAfter(testDate, maxValue);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotAfter_WithDateEqualToMaxValue_ReturnsSuccess()
    {
        var maxValue = DateTimeOffset.UtcNow.AddDays(1);
        var result = global::Nordstein.Core.Common.Validation.Validation.NotAfter(maxValue, maxValue);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void NotAfter_WithDateAfterMaxValue_ReturnsError()
    {
        var maxValue = DateTimeOffset.UtcNow.AddDays(1);
        var testDate = DateTimeOffset.UtcNow.AddDays(5);
        var result = global::Nordstein.Core.Common.Validation.Validation.NotAfter(testDate, maxValue);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("cannot be after");
    }

    [TestMethod]
    public void NotAfter_IncludesMemberName()
    {
        var maxValue = DateTimeOffset.UtcNow.AddDays(1);
        var result = global::Nordstein.Core.Common.Validation.Validation.NotAfter(DateTimeOffset.UtcNow.AddDays(5), maxValue);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Positive_WithPositiveValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Positive(1m);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void Positive_WithZero_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Positive(0m);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be positive");
    }

    [TestMethod]
    public void Positive_WithNegativeValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Positive(-1m);
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void Positive_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Positive(0m);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void LessThan_WithSmallerValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThan(5m, 10m);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void LessThan_WithEqualValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThan(10m, 10m);
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void LessThan_WithLargerValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThan(15m, 10m);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be less than");
    }

    [TestMethod]
    public void LessThan_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThan(15m, 10m);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void LessThanOrEqual_WithSmallerValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThanOrEqual(5m, 10m);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void LessThanOrEqual_WithEqualValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThanOrEqual(10m, 10m);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void LessThanOrEqual_WithLargerValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThanOrEqual(15m, 10m);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be less than or equal to");
    }

    [TestMethod]
    public void LessThanOrEqual_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.LessThanOrEqual(15m, 10m);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void GreaterThan_WithLargerValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThan(10m, 5m);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void GreaterThan_WithEqualValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThan(5m, 5m);
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void GreaterThan_WithSmallerValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThan(3m, 5m);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be greater than");
    }

    [TestMethod]
    public void GreaterThan_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThan(3m, 5m);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void GreaterThanOrEqual_WithLargerValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThanOrEqual(10m, 5m);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void GreaterThanOrEqual_WithEqualValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThanOrEqual(5m, 5m);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void GreaterThanOrEqual_WithSmallerValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThanOrEqual(3m, 5m);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be greater than or equal to");
    }

    [TestMethod]
    public void GreaterThanOrEqual_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.GreaterThanOrEqual(3m, 5m);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void ExactLength_WithMatchingLength_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ExactLength("abc", 3);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void ExactLength_WithShorterString_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ExactLength("ab", 3);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be exactly 3 characters");
    }

    [TestMethod]
    public void ExactLength_WithLongerString_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ExactLength("abcd", 3);
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void ExactLength_WithNull_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ExactLength(null, 3);
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void ExactLength_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ExactLength("ab", 3);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Matches_WithMatchingPattern_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Matches("hello123", @"^[a-z0-9]+$");
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void Matches_WithNonMatchingPattern_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Matches("hello!", @"^[a-z0-9]+$");
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("does not match the required pattern");
    }

    [TestMethod]
    public void Matches_WithNull_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Matches(null, @"^[a-z]+$");
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void Matches_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Matches("hello!", @"^[a-z0-9]+$");
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void ValidUri_WithValidAbsoluteUri_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ValidUri("https://example.com/api");
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void ValidUri_WithHttpUri_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ValidUri("http://localhost:5001");
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void ValidUri_WithRelativeUri_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ValidUri("/relative/path");
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("must be a valid absolute URI");
    }

    [TestMethod]
    public void ValidUri_WithNull_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ValidUri(null);
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void ValidUri_WithGarbageString_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ValidUri("not a uri at all!!!");
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void ValidUri_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.ValidUri(null);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Defined_WithDefinedEnumValue_ReturnsSuccess()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Defined(DayOfWeek.Monday);
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void Defined_WithUndefinedEnumValue_ReturnsError()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Defined((DayOfWeek)999);
        result.Should().NotBe(ValidationResult.Success);
        result.ErrorMessage.Should().Contain("has an undefined value");
    }

    [TestMethod]
    public void Defined_IncludesMemberName()
    {
        var result = global::Nordstein.Core.Common.Validation.Validation.Defined((DayOfWeek)999);
        result.MemberNames.Should().NotBeEmpty();
    }

    [TestMethod]
    public void ValidationResults_IncludeMemberName()
    {
        // Arrange
        var result = global::Nordstein.Core.Common.Validation.Validation.NotNullOrWhiteSpace(string.Empty);

        // Assert
        result.MemberNames.Should().NotBeEmpty();
        // CallerArgumentExpression captures the argument expression at the call site
        result.MemberNames.Should().Contain("string.Empty");
    }

    [TestMethod]
    public void NotDefault_WithCustomType_ReturnsError()
    {
        // Arrange
        var defaultValue = default(TestStruct);

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(defaultValue);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBe(ValidationResult.Success);
    }

    [TestMethod]
    public void NotDefault_WithNonDefaultCustomType_ReturnsSuccess()
    {
        // Arrange
        var nonDefaultValue = new TestStruct { Value = 42 };

        // Act
        var result = global::Nordstein.Core.Common.Validation.Validation.NotDefault(nonDefaultValue);

        // Assert
        result.Should().Be(ValidationResult.Success);
    }

    [TestMethod]
    public void Validate_WithValidObject_DoesNotThrow()
    {
        // Arrange
        var validObject = new ValidatableTestObject { Name = "Valid Name", Age = 25 };

        // Act & Assert
        var action = () => validObject.Validate();
        action.Should().NotThrow();
    }

    [TestMethod]
    public void Validate_WithInvalidObject_ThrowsValidationException()
    {
        // Arrange
        var invalidObject = new ValidatableTestObject { Name = "", Age = 25 };

        // Act & Assert
        var action = () => invalidObject.Validate();
        action.Should().Throw<ValidationException>();
    }

    [TestMethod]
    public void Validate_WithMultipleValidationErrors_ThrowsValidationException()
    {
        // Arrange
        var invalidObject = new ValidatableTestObject { Name = "", Age = -5 };

        // Act & Assert
        var action = () => invalidObject.Validate();
        action.Should().Throw<ValidationException>();
    }

    private class ValidatableTestObject : IValidatableObject
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult("Name is required", [nameof(Name)]);
            }

            if (Age < 0)
            {
                yield return new ValidationResult("Age must be non-negative", [nameof(Age)]);
            }
        }
    }

    private struct TestStruct
    {
        public int Value { [UsedImplicitly] get; set; }
    }
}
