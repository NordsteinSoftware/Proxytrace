using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.MfaBackupCode;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class MfaBackupCodeValidationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidArgs_CreatesUnconsumedCode()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IMfaBackupCode.CreateNew>();

        var code = factory(user, "hash-abcdef");

        code.User.Id.Should().Be(user.Id);
        code.CodeHash.Should().Be("hash-abcdef");
        code.ConsumedAt.Should().BeNull();
        code.IsConsumed.Should().BeFalse();
        code.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyHash_Throws()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IMfaBackupCode.CreateNew>();

        var act = () => factory(user, "");
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task MarkConsumed_SetsConsumedAtAndPersists()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IMfaBackupCode.CreateNew>();
        var code = await factory(user, "hash-abcdef").AddAsync(CancellationToken);

        var consumed = await code.MarkConsumedAsync(CancellationToken);

        consumed.ConsumedAt.Should().NotBeNull();
        consumed.IsConsumed.Should().BeTrue();

        var reloaded = await services.GetRequiredService<IRepository<IMfaBackupCode>>().GetAsync(code.Id, CancellationToken);
        reloaded.ConsumedAt.Should().NotBeNull();
    }
}
