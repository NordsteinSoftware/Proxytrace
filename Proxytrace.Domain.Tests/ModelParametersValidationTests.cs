using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Inference;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ModelParametersValidationTests : BaseTest<Module>
{
    [TestMethod]
    public void Create_WithValidParameters_Succeeds()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var parameters = factory(
            temperature: 0.7,
            topP: 0.9,
            frequencyPenalty: 0.5,
            presencePenalty: -0.5,
            maxTokens: 256,
            n: 1);

        parameters.MaxTokens.Should().Be(256);
        parameters.N.Should().Be(1);
    }

    [TestMethod]
    public void Create_WithEmptyParameters_Succeeds()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory();

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Create_WithNegativeMaxTokens_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory(maxTokens: -5);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Create_WithZeroN_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory(n: 0);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Create_WithNaNTemperature_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory(temperature: double.NaN);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Create_WithNegativeTemperature_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory(temperature: -0.1);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Create_WithTopPAboveOne_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory(topP: 1.5);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Create_WithInfinitePresencePenalty_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory(presencePenalty: double.PositiveInfinity);

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Create_WithFrequencyPenaltyOutOfRange_Throws()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<IModelParameters.Create>();

        var act = () => factory(frequencyPenalty: 3.0);

        act.Should().Throw<Exception>();
    }
}
