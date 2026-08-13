using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.ApplicationError;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ApplicationErrorValidationTests : BaseTest<Module>
{
    [TestMethod]
    public void CreateNew_WithValidArgs_CreatesError()
    {
        var services = GetServices();
        var factory = services.GetRequiredService<IApplicationError.CreateNew>();

        var error = factory("boom", ApplicationErrorLevel.Critical, "Proxytrace.Some.Category", "System.Exception", "   at X()");

        error.Id.Should().NotBe(Guid.Empty);
        error.Message.Should().Be("boom");
        error.Level.Should().Be(ApplicationErrorLevel.Critical);
        error.Category.Should().Be("Proxytrace.Some.Category");
        error.ExceptionType.Should().Be("System.Exception");
        error.StackTrace.Should().Be("   at X()");
    }

    [TestMethod]
    public void CreateNew_WithNullExceptionDetails_IsAllowed()
    {
        var services = GetServices();
        var factory = services.GetRequiredService<IApplicationError.CreateNew>();

        var error = factory("plain log", ApplicationErrorLevel.Error, "Cat", null, null);

        error.ExceptionType.Should().BeNull();
        error.StackTrace.Should().BeNull();
    }

    [TestMethod]
    public void CreateNew_WithEmptyMessage_Throws()
    {
        var services = GetServices();
        var factory = services.GetRequiredService<IApplicationError.CreateNew>();

        var act = () => factory("", ApplicationErrorLevel.Error, "Cat", null, null);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void CreateNew_WithEmptyCategory_Throws()
    {
        var services = GetServices();
        var factory = services.GetRequiredService<IApplicationError.CreateNew>();

        var act = () => factory("boom", ApplicationErrorLevel.Error, "", null, null);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void CreateNew_CalledTwice_ProducesUniqueIds()
    {
        var services = GetServices();
        var factory = services.GetRequiredService<IApplicationError.CreateNew>();

        var a = factory("boom", ApplicationErrorLevel.Error, "Cat", null, null);
        var b = factory("boom", ApplicationErrorLevel.Error, "Cat", null, null);

        a.Id.Should().NotBe(b.Id);
    }

    [TestMethod]
    public void CreateExisting_RoundTrips_AllProperties()
    {
        var services = GetServices();
        var createNew = services.GetRequiredService<IApplicationError.CreateNew>();
        var createExisting = services.GetRequiredService<IApplicationError.CreateExisting>();

        var original = createNew("boom", ApplicationErrorLevel.Critical, "Cat", "System.Exception", "stack");
        var restored = createExisting(
            original.Message,
            original.Level,
            original.Category,
            original.ExceptionType,
            original.StackTrace,
            original);

        restored.Id.Should().Be(original.Id);
        restored.CreatedAt.Should().Be(original.CreatedAt);
        restored.UpdatedAt.Should().Be(original.UpdatedAt);
        restored.Message.Should().Be(original.Message);
        restored.Level.Should().Be(original.Level);
        restored.Category.Should().Be(original.Category);
        restored.ExceptionType.Should().Be(original.ExceptionType);
        restored.StackTrace.Should().Be(original.StackTrace);
    }
}
