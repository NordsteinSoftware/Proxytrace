using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Proxytrace.Application.Auth.Local;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth.Local;

[TestClass]
public sealed class TotpServiceTests : BaseTest<Module>
{
    private static string ComputeCode(string secret)
        => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

    [TestMethod]
    public void GenerateSecret_ProducesDistinctDecodableSecrets()
    {
        var svc = GetServices().GetRequiredService<ITotpService>();

        var a = svc.GenerateSecret();
        var b = svc.GenerateSecret();

        a.Should().NotBeNullOrEmpty();
        a.Should().NotBe(b);
        // Must be valid Base32 (decoding throws otherwise).
        Base32Encoding.ToBytes(a).Length.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void BuildOtpAuthUri_ContainsSecretIssuerAndEmail()
    {
        var svc = GetServices().GetRequiredService<ITotpService>();
        var secret = svc.GenerateSecret();

        var uri = svc.BuildOtpAuthUri("user@example.test", secret);

        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain($"secret={secret}");
        uri.Should().Contain("issuer=Proxytrace");
        uri.Should().Contain(Uri.EscapeDataString("user@example.test"));
    }

    [TestMethod]
    public void TryVerify_WithCurrentCode_Succeeds()
    {
        var svc = GetServices().GetRequiredService<ITotpService>();
        var secret = svc.GenerateSecret();

        svc.TryVerify(secret, ComputeCode(secret), lastUsedStep: null, out var step).Should().BeTrue();
        step.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void TryVerify_WithWrongCode_Fails()
    {
        var svc = GetServices().GetRequiredService<ITotpService>();
        var secret = svc.GenerateSecret();

        svc.TryVerify(secret, "000000", lastUsedStep: null, out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryVerify_ReplayOfSameStep_Fails()
    {
        var svc = GetServices().GetRequiredService<ITotpService>();
        var secret = svc.GenerateSecret();
        var code = ComputeCode(secret);

        svc.TryVerify(secret, code, lastUsedStep: null, out var step).Should().BeTrue();
        // The same code, now that its step is recorded as used, must be rejected as a replay.
        svc.TryVerify(secret, code, lastUsedStep: step, out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryVerify_WithEmptyInputs_Fails()
    {
        var svc = GetServices().GetRequiredService<ITotpService>();
        var secret = svc.GenerateSecret();

        svc.TryVerify(secret, "", lastUsedStep: null, out _).Should().BeFalse();
        svc.TryVerify("", "123456", lastUsedStep: null, out _).Should().BeFalse();
    }
}
