using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth.Local;

[TestClass]
public sealed class PasswordServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task HashThenVerify_RoundTrips()
    {
        var services = GetServices();
        var svc = services.GetRequiredService<IPasswordService>();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var hash = svc.Hash(user, "Abcdef1!");

        svc.Verify(user, hash, "Abcdef1!").Should().BeTrue();
        svc.Verify(user, hash, "Wrong!1A").Should().BeFalse();
    }
}
