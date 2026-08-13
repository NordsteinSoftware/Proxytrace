using Autofac;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Security;
using Proxytrace.Infrastructure.Security.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Security;

[TestClass]
public sealed class SecretProtectorTests : BaseTest<Module>
{
    [TestMethod]
    public void Unprotect_OfProtect_RoundTripsPlaintext()
    {
        var protector = Resolve();

        var cipher = protector.Protect("hunter2");

        cipher.Should().NotBe("hunter2");
        protector.Unprotect(cipher).Should().Be("hunter2");
    }

    [TestMethod]
    public void Protect_ProducesDifferentCiphertextFromPlaintext()
    {
        var protector = Resolve();

        protector.Protect("smtp-password").Should().NotContain("smtp-password");
    }

    [TestMethod]
    public void Protect_IsNonDeterministic()
    {
        var protector = Resolve();

        // ASP.NET Data Protection uses a random IV per call, so the same plaintext
        // must never yield identical ciphertext — both must still decrypt back.
        var first = protector.Protect("smtp-password");
        var second = protector.Protect("smtp-password");

        first.Should().NotBe(second);
        protector.Unprotect(first).Should().Be("smtp-password");
        protector.Unprotect(second).Should().Be("smtp-password");
    }

    private ISecretProtector Resolve()
    {
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterServiceCollection(s => s.AddDataProtection());
            builder.RegisterType<DataProtectionSecretProtector>().As<ISecretProtector>().SingleInstance();
        });
        return services.GetRequiredService<ISecretProtector>();
    }
}
