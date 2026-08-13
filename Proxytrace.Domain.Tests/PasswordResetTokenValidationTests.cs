using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.PasswordResetToken;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class PasswordResetTokenValidationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidArgs_CreatesToken()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IPasswordResetToken.CreateNew>();

        var token = factory(user, "hash-abcdef", DateTimeOffset.UtcNow.AddHours(1));

        token.User.Id.Should().Be(user.Id);
        token.TokenHash.Should().Be("hash-abcdef");
        token.ConsumedAt.Should().BeNull();
        token.IsConsumed.Should().BeFalse();
        token.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyTokenHash_Throws()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IPasswordResetToken.CreateNew>();

        var act = () => factory(user, "", DateTimeOffset.UtcNow.AddHours(1));
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_CalledTwice_ProducesDistinctIds()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IPasswordResetToken.CreateNew>();

        var a = factory(user, "h", DateTimeOffset.UtcNow.AddHours(1));
        var b = factory(user, "h", DateTimeOffset.UtcNow.AddHours(1));

        a.Id.Should().NotBe(b.Id);
    }

    [TestMethod]
    public async Task IsExpired_ReflectsExpiry()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IPasswordResetToken.CreateNew>();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        var token = factory(user, "h", expiresAt);

        token.IsExpired(expiresAt.AddMinutes(-1)).Should().BeFalse();
        token.IsExpired(expiresAt.AddMinutes(1)).Should().BeTrue();
    }

    [TestMethod]
    public async Task MarkConsumed_SetsConsumedAtAndPersists()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IPasswordResetToken.CreateNew>();
        var token = await factory(user, "h", DateTimeOffset.UtcNow.AddHours(1)).AddAsync(CancellationToken);

        var consumed = await token.MarkConsumedAsync(CancellationToken);

        consumed.ConsumedAt.Should().NotBeNull();
        consumed.IsConsumed.Should().BeTrue();

        var reloaded = await services.GetRequiredService<IRepository<IPasswordResetToken>>().GetAsync(token.Id, CancellationToken);
        reloaded.ConsumedAt.Should().NotBeNull();
    }
}
