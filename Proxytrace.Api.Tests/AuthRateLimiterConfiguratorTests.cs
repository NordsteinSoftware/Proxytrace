using System.Net;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Proxytrace.Api.Controllers;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class AuthRateLimiterConfiguratorTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    private static string? PolicyOf(string action) =>
        typeof(AuthController).GetMethod(action)?
            .GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

    [TestMethod]
    public void LoginLimits_ByDefault_AllowThirtyAttemptsPerMinute()
    {
        var limits = new AuthRateLimiterConfigurator(Configuration()).LoginLimits();

        limits.PermitLimit.Should().Be(30);
        limits.Window.Should().Be(TimeSpan.FromMinutes(1));
        limits.QueueLimit.Should().Be(0);
    }

    [TestMethod]
    public void LoginLimits_WhenConfigured_UseTheOperatorValues()
    {
        var limits = new AuthRateLimiterConfigurator(Configuration(
            ("RateLimiting:Login:PermitLimit", "5"),
            ("RateLimiting:Login:WindowSeconds", "120"))).LoginLimits();

        limits.PermitLimit.Should().Be(5);
        limits.Window.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void PasswordResetLimits_ByDefault_KeepTheDocumentedTenPerQuarterHour()
    {
        var limits = new AuthRateLimiterConfigurator(Configuration()).PasswordResetLimits();

        limits.PermitLimit.Should().Be(10);
        limits.Window.Should().Be(TimeSpan.FromMinutes(15));
    }

    [TestMethod]
    public void MfaLimits_ByDefault_KeepTheDocumentedTenPerQuarterHour()
    {
        var limits = new AuthRateLimiterConfigurator(Configuration()).MfaLimits();

        limits.PermitLimit.Should().Be(10);
        limits.Window.Should().Be(TimeSpan.FromMinutes(15));
    }

    [TestMethod]
    public void PartitionKey_ForDifferentClientAddresses_SeparatesTheBuckets()
    {
        var configurator = new AuthRateLimiterConfigurator(Configuration());
        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var second = new DefaultHttpContext();
        second.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");

        configurator.PartitionKey(first).Should().NotBe(configurator.PartitionKey(second));
    }

    [TestMethod]
    public void PartitionKey_WithoutARemoteAddress_FallsBackToASharedBucket()
    {
        var configurator = new AuthRateLimiterConfigurator(Configuration());

        configurator.PartitionKey(new DefaultHttpContext()).Should().Be("unknown");
    }

    [TestMethod]
    public void Configure_Always_RejectsWithTooManyRequests()
    {
        var options = new RateLimiterOptions();

        new AuthRateLimiterConfigurator(Configuration()).Configure(options);

        options.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [TestMethod]
    public void Login_IsRateLimited()
    {
        // Without this the only bound on password guessing is request throughput — there is no
        // per-account failed-attempt counter anywhere in the auth stack.
        PolicyOf(nameof(AuthController.Login)).Should().Be(AuthRateLimiterConfigurator.LoginPolicy);
    }

    [TestMethod]
    public void ClaimLegacy_IsRateLimited()
    {
        PolicyOf(nameof(AuthController.ClaimLegacy)).Should().Be(AuthRateLimiterConfigurator.LoginPolicy);
    }

    [TestMethod]
    public void Signup_IsRateLimited()
    {
        PolicyOf(nameof(AuthController.Signup)).Should().Be(AuthRateLimiterConfigurator.LoginPolicy);
    }

    [TestMethod]
    public void InvitePreview_IsRateLimited()
    {
        PolicyOf(nameof(AuthController.Preview)).Should().Be(AuthRateLimiterConfigurator.LoginPolicy);
    }

    [TestMethod]
    public void PasswordResetEndpoints_KeepTheirRateLimit()
    {
        PolicyOf(nameof(AuthController.ForgotPassword)).Should().Be(AuthRateLimiterConfigurator.PasswordResetPolicy);
        PolicyOf(nameof(AuthController.ResetPassword)).Should().Be(AuthRateLimiterConfigurator.PasswordResetPolicy);
    }

    [TestMethod]
    public void MfaVerify_KeepsItsRateLimit()
    {
        PolicyOf(nameof(AuthController.MfaVerify)).Should().Be(AuthRateLimiterConfigurator.MfaPolicy);
    }

    [TestMethod]
    public void PolicyNames_MatchTheDocumentedIdentifiers()
    {
        AuthRateLimiterConfigurator.LoginPolicy.Should().Be("auth-login");
        AuthRateLimiterConfigurator.PasswordResetPolicy.Should().Be("auth-reset");
        AuthRateLimiterConfigurator.MfaPolicy.Should().Be("auth-mfa");
    }
}
