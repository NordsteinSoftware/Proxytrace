using System.IdentityModel.Tokens.Jwt;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth.Local;

[TestClass]
public sealed class LocalTokenIssuerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Issue_ProducesValidJwt()
    {
        var services = GetServices();
        var issuer = services.GetRequiredService<ILocalTokenIssuer>();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var result = issuer.Issue(user);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        token.Subject.Should().Be(user.Id.ToString());
        token.Claims.Single(c => c.Type == "email").Value.Should().Be(user.Email);
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddHours(1));
    }
}
