using AwesomeAssertions;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Demo;

[TestClass]
public sealed class KioskEndpointOptionsTests : BaseTest<Module>
{
    [TestMethod]
    public void IsConfigured_WhenAllRequiredFieldsPresent_IsTrue()
    {
        var options = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-test",
            Model = "gpt-4o",
        };

        options.IsConfigured.Should().BeTrue();
    }

    [TestMethod]
    public void IsConfigured_WhenApiKeyMissing_IsFalse()
    {
        var options = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o",
        };

        options.IsConfigured.Should().BeFalse();
    }

    [TestMethod]
    public void Resolve_WithCompleteConfig_ReturnsResolvedEndpoint()
    {
        var options = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-real",
            Model = "gpt-4o",
            Kind = "openaicompatible",
            ProviderName = "Demo Provider",
            InputTokenCost = 0.000003m,
            OutputTokenCost = 0.000015m,
        };

        var resolved = options.Resolve();

        resolved.BaseUrl.Should().Be(new Uri("https://api.openai.com/v1"));
        resolved.ApiKey.Should().Be("sk-real");
        resolved.Model.Should().Be("gpt-4o");
        resolved.Kind.Should().Be(ModelProviderKind.OpenAiCompatible);
        resolved.ProviderName.Should().Be("Demo Provider");
        resolved.InputTokenCost.Should().Be(0.000003m);
        resolved.OutputTokenCost.Should().Be(0.000015m);
    }

    [TestMethod]
    public void HasAnyCredential_WhenAllCredentialFieldsBlank_IsFalse()
    {
        // Mirrors the showcase compose's env-less defaults: empty BaseUrl/ApiKey/Model plus the
        // non-empty Kind/ProviderName defaults. This must read as "no endpoint" (read-only kiosk).
        var options = new KioskEndpointOptions
        {
            BaseUrl = "",
            ApiKey = "  ",
            Model = null,
        };

        options.HasAnyCredential.Should().BeFalse();
        options.IsConfigured.Should().BeFalse();
    }

    [TestMethod]
    public void HasAnyCredential_WhenSomeCredentialFieldSet_IsTrue()
    {
        var options = new KioskEndpointOptions
        {
            Model = "gpt-4o",
        };

        options.HasAnyCredential.Should().BeTrue();
    }

    [TestMethod]
    public void GatedResolve_WhenSectionAllBlank_SkipsResolveAndDoesNotThrow()
    {
        // The composition root only calls Resolve() when HasAnyCredential is true. Reproduce that gate
        // to prove an env-less kiosk boots (read-only) instead of fail-fasting on a partial config.
        var options = new KioskEndpointOptions
        {
            BaseUrl = "",
            ApiKey = "",
            Model = "",
            Kind = "OpenAi",
        };

        FluentActions.Invoking(() =>
            {
                if (options.HasAnyCredential)
                {
                    options.Resolve();
                }
            })
            .Should().NotThrow();
    }

    [TestMethod]
    public void GatedResolve_WhenSectionPartiallyConfigured_Throws()
    {
        // At least one credential set but not all: the gate calls Resolve(), which must fail fast.
        var options = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o",
        };

        options.HasAnyCredential.Should().BeTrue();
        FluentActions.Invoking(() =>
            {
                if (options.HasAnyCredential)
                {
                    options.Resolve();
                }
            })
            .Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Resolve_WhenRequiredFieldMissing_Throws()
    {
        var options = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o",
        };

        FluentActions.Invoking(() => options.Resolve())
            .Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Resolve_WhenBaseUrlNotAbsolute_Throws()
    {
        var options = new KioskEndpointOptions
        {
            BaseUrl = "not-a-url",
            ApiKey = "sk-real",
            Model = "gpt-4o",
        };

        FluentActions.Invoking(() => options.Resolve())
            .Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Resolve_WhenKindInvalid_Throws()
    {
        var options = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-real",
            Model = "gpt-4o",
            Kind = "Bogus",
        };

        FluentActions.Invoking(() => options.Resolve())
            .Should().Throw<InvalidOperationException>();
    }
}
