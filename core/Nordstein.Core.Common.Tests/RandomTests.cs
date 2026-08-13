using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class RandomTests : BaseTest<Module>
{
    private IRandom Random => GetServices().GetRequiredService<IRandom>();

    [TestMethod]
    public void SeededRandom_GeneratesSameValuesForSameSeed()
    {
        var value1 = Random.Int(min: 0, max: 100);
        var value2 = Random.Int(min: 0, max: 100);
        
        value1.Should().Be(value2);
    }
    
    [TestMethod]
    public void Bool_CanReturnTrueOrFalse()
    {
        // With multiple calls to the random, we should eventually see both true and false
        var results = new HashSet<bool>();
        var random = Random;
        for (int i = 0; i < 100; i++)
        {
            results.Add(random.Bool());
            if (results.Count == 2)
            {
                break;
            }
        }
        
        results.Count.Should().Be(2);
    }
    
    [TestMethod]
    public void Guid_ReturnsValidGuid()
    {
        var guid = Random.Guid();
        guid.Should().NotBe(Guid.Empty);
    }
    
    [TestMethod]
    public void Guid_GeneratesDifferentGuidsOnSuccessiveCalls()
    {
        var services = GetServices();
        var guid1 = services.GetRequiredService<IRandom>().Guid();
        var guid2 = services.GetRequiredService<IRandom>().Guid();
        
        guid1.Should().NotBe(guid2);
    }
    
    [TestMethod]
    public void String_ReturnsNonEmptyString()
    {
        var result = Random.String();
        result.Should().NotBeNullOrWhiteSpace();
    }
    
    [TestMethod]
    public void String_ReturnsStringComposedOfWords()
    {
        var result = Random.String();

        result.Should().NotBeNullOrWhiteSpace();
        result.Split(' ').Length.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void String_GeneratesDifferentStringsOnSuccessiveCalls()
    {
        var results = new HashSet<string>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.String());
        }

        results.Count.Should().BeGreaterThan(1);
    }
    
    [TestMethod]
    public void UniqueString_ReturnsNonEmptyString()
    {
        var result = Random.UniqueString();
        
        result.Should().NotBeNullOrWhiteSpace();
    }
    
    [TestMethod]
    public void UniqueString_ReturnsUniqueStringsOnSuccessiveCalls()
    {
        var strings = new HashSet<string>();

        var random = Random;
        for (int i = 0; i < 100; i++)
        {
            strings.Add(random.UniqueString());
        }
        
        strings.Count.Should().Be(100);
    }
    
    [TestMethod]
    public void UniqueString_ReturnsGuidFormatString()
    {
        var result = Random.UniqueString();
        
        // UniqueString returns a GUID string
        Guid.TryParse(result, out _).Should().BeTrue();
    }
    
    [TestMethod]
    public void Int_WithNoParameters_ReturnsNonNegativeInt()
    {
        var result = Random.Int();
        
        result.Should().BeGreaterThanOrEqualTo(0);
    }
    
    [TestMethod]
    public void Int_WithMinAndMax_ReturnsValueInRange()
    {
        var results = new List<int>();
        for (int i = 0; i < 50; i++)
        {
            results.Add(Random.Int(min: 10, max: 20));
        }
        
        results.Should().AllSatisfy(r => r.Should().BeGreaterThanOrEqualTo(10));
        results.Should().AllSatisfy(r => r.Should().BeLessThan(20));
    }
    
    [TestMethod]
    public void Int_WithMin_ReturnsValueGreaterThanOrEqualToMin()
    {
        var result = Random.Int(min: 50);
        
        result.Should().BeGreaterThanOrEqualTo(50);
    }
    
    [TestMethod]
    public void Int_WithMax_ReturnsValueLessThanMax()
    {
        var result = Random.Int(max: 50);
        
        result.Should().BeLessThan(50);
        result.Should().BeGreaterThanOrEqualTo(0);
    }
    
    [TestMethod]
    public void Int_WithSameRange_CanGenerateMultipleValues()
    {
        var results = new HashSet<int>();
        var random = Random;
        for (int i = 0; i < 100; i++)
        {
            results.Add(random.Int(min: 1, max: 100));
        }
        
        // Should generate more than one unique value
        results.Count.Should().BeGreaterThan(1);
    }
    
    [TestMethod]
    public void Any_ReturnsElementFromCollection()
    {
        var options = new List<string> { "apple", "banana", "cherry" };
        
        var result = Random.Any(options);
        
        options.Should().Contain(result);
    }
    
    [TestMethod]
    public void Any_CanReturnDifferentElements()
    {
        var random = Random; // Cache the instance to avoid getting new seeded instance each time
        var options = new List<int> { 1, 2, 3, 4, 5 };
        var results = new HashSet<int>();
        
        for (int i = 0; i < 100; i++)
        {
            results.Add(random.Any(options));
        }
        
        // Should return more than one distinct value over multiple calls
        results.Count.Should().BeGreaterThan(1);
    }
    
    [TestMethod]
    public void Any_WithSingleElement_ReturnsThatElement()
    {
        var options = new List<string> { "only" };
        
        var result = Random.Any(options);
        
        result.Should().Be("only");
    }
    
    [TestMethod]
    public void Any_WithGenericType_ReturnsCorrectType()
    {
        var options = new List<DateTimeOffset>
        {
            DateTimeOffset.Now,
            DateTimeOffset.UtcNow.AddDays(1)
        };

        var result = Random.Any(options);

        options.Should().Contain(result);
    }

    [TestMethod]
    public void Long_WithNoParameters_ReturnsNonNegativeLong()
    {
        var result = Random.Long();

        result.Should().BeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    public void Long_WithMinAndMax_ReturnsValueInRange()
    {
        var results = new List<long>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.Long(min: 100, max: 200));
        }

        results.Should().AllSatisfy(r => r.Should().BeGreaterThanOrEqualTo(100));
        results.Should().AllSatisfy(r => r.Should().BeLessThan(200));
    }

    [TestMethod]
    public void Long_WithMin_ReturnsValueGreaterThanOrEqualToMin()
    {
        var result = Random.Long(min: 1_000_000_000L);

        result.Should().BeGreaterThanOrEqualTo(1_000_000_000L);
    }

    [TestMethod]
    public void Long_WithMax_ReturnsValueLessThanMax()
    {
        var result = Random.Long(max: 50);

        result.Should().BeLessThan(50);
        result.Should().BeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    public void Long_CanGenerateMultipleValues()
    {
        var results = new HashSet<long>();
        var random = Random;
        for (int i = 0; i < 100; i++)
        {
            results.Add(random.Long(min: 1, max: 1000));
        }

        results.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void Decimal_WithNoParameters_ReturnsNonNegativeValue()
    {
        var result = Random.Decimal();

        result.Should().BeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    public void Decimal_WithMinAndMax_ReturnsValueInRange()
    {
        var results = new List<decimal>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.Decimal(min: 1.5m, max: 9.5m));
        }

        results.Should().AllSatisfy(r => r.Should().BeGreaterThanOrEqualTo(1.5m));
        results.Should().AllSatisfy(r => r.Should().BeLessThan(9.5m));
    }

    [TestMethod]
    public void Decimal_WithMin_ReturnsValueGreaterThanOrEqualToMin()
    {
        var result = Random.Decimal(min: 100m);

        result.Should().BeGreaterThanOrEqualTo(100m);
    }

    [TestMethod]
    public void Decimal_WithMax_ReturnsValueLessThanMax()
    {
        var result = Random.Decimal(max: 1m);

        result.Should().BeLessThan(1m);
        result.Should().BeGreaterThanOrEqualTo(0m);
    }

    [TestMethod]
    public void Decimal_CanGenerateMultipleValues()
    {
        var results = new HashSet<decimal>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.Decimal(min: 0m, max: 100m));
        }

        results.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void Double_WithNoParameters_ReturnsBetweenZeroAndOne()
    {
        var results = new List<double>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.Double());
        }

        results.Should().AllSatisfy(r => r.Should().BeGreaterThanOrEqualTo(0.0));
        results.Should().AllSatisfy(r => r.Should().BeLessThan(1.0));
    }

    [TestMethod]
    public void Double_WithMinAndMax_ReturnsValueInRange()
    {
        var results = new List<double>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.Double(min: 5.0, max: 10.0));
        }

        results.Should().AllSatisfy(r => r.Should().BeGreaterThanOrEqualTo(5.0));
        results.Should().AllSatisfy(r => r.Should().BeLessThan(10.0));
    }

    [TestMethod]
    public void Double_WithMin_ReturnsValueGreaterThanOrEqualToMin()
    {
        var result = Random.Double(min: 0.5);

        result.Should().BeGreaterThanOrEqualTo(0.5);
    }

    [TestMethod]
    public void Double_WithMax_ReturnsValueLessThanMax()
    {
        var result = Random.Double(max: 0.5);

        result.Should().BeLessThan(0.5);
        result.Should().BeGreaterThanOrEqualTo(0.0);
    }

    [TestMethod]
    public void Double_CanGenerateMultipleValues()
    {
        var results = new HashSet<double>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.Double(min: 0.0, max: 1.0));
        }

        results.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void TimeSpan_WithNoParameters_ReturnsPositiveSpan()
    {
        var result = Random.TimeSpan();

        result.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [TestMethod]
    public void TimeSpan_WithMinAndMax_ReturnsValueInRange()
    {
        var min = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(10);
        var results = new List<TimeSpan>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.TimeSpan(min: min, max: max));
        }

        results.Should().AllSatisfy(r => r.Should().BeGreaterThanOrEqualTo(min));
        results.Should().AllSatisfy(r => r.Should().BeLessThan(max));
    }

    [TestMethod]
    public void TimeSpan_WithMin_ReturnsValueGreaterThanOrEqualToMin()
    {
        var min = TimeSpan.FromMinutes(1);

        var result = Random.TimeSpan(min: min, max: TimeSpan.FromMinutes(5));

        result.Should().BeGreaterThanOrEqualTo(min);
    }

    [TestMethod]
    public void TimeSpan_CanGenerateMultipleValues()
    {
        var results = new HashSet<TimeSpan>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.TimeSpan(min: TimeSpan.Zero, max: TimeSpan.FromHours(1)));
        }

        results.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void Enum_ReturnsValidEnumValue()
    {
        var result = Random.Enum<DayOfWeek>();

        Enum.IsDefined(result).Should().BeTrue();
    }

    [TestMethod]
    public void Enum_CanReturnDifferentValues()
    {
        var results = new HashSet<DayOfWeek>();
        var random = Random;
        for (int i = 0; i < 100; i++)
        {
            results.Add(random.Enum<DayOfWeek>());
        }

        results.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void DateTimeOffset_WithNoParameters_ReturnsRecentDate()
    {
        var result = Random.DateTimeOffset();

        result.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
        result.Should().BeAfter(DateTimeOffset.UtcNow.AddYears(-1).AddSeconds(-1));
    }

    [TestMethod]
    public void DateTimeOffset_WithMinAndMax_ReturnsValueInRange()
    {
        var min = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var max = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var results = new List<DateTimeOffset>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.DateTimeOffset(min: min, max: max));
        }

        results.Should().AllSatisfy(r => r.Should().BeOnOrAfter(min));
        results.Should().AllSatisfy(r => r.Should().BeOnOrBefore(max));
    }

    [TestMethod]
    public void DateTimeOffset_CanGenerateMultipleValues()
    {
        var results = new HashSet<DateTimeOffset>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            results.Add(random.DateTimeOffset());
        }

        results.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void Uri_ReturnsValidUri()
    {
        var result = Random.Uri();

        result.Should().NotBeNull();
        result.IsAbsoluteUri.Should().BeTrue();
        result.Scheme.Should().Be("https");
    }

    [TestMethod]
    public void Uri_GeneratesDifferentUrisOnSuccessiveCalls()
    {
        var uris = new HashSet<string>();
        var random = Random;
        for (int i = 0; i < 20; i++)
        {
            uris.Add(random.Uri().ToString());
        }

        uris.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void Email_ReturnsValidEmailFormat()
    {
        var result = Random.Email();

        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("@");
        result.Split('@').Length.Should().Be(2);
        result.Split('@')[1].Should().Contain(".");
    }

    [TestMethod]
    public void Email_GeneratesDifferentEmailsOnSuccessiveCalls()
    {
        var emails = new HashSet<string>();
        var random = Random;
        for (int i = 0; i < 50; i++)
        {
            emails.Add(random.Email());
        }

        emails.Count.Should().BeGreaterThan(1);
    }
}