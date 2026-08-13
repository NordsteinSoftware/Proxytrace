using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class UserValidationTests : BaseTest<Module>
{
    private const string ValidEmail = "user@example.com";
    private const string ValidSubject = "issuer|sub-123";
    private const string ValidHash = "AQAAAAEAACcQAAAAEHashValue";

    [TestMethod]
    public void CreateNew_OidcUser_CreatesUser()
    {
        var factory = GetServices().GetRequiredService<IUser.CreateNew>();
        var u = factory(ValidEmail, ValidSubject, null, UserRole.Member);
        u.Email.Should().Be(ValidEmail);
        u.ExternalSubject.Should().Be(ValidSubject);
        u.PasswordHash.Should().BeNull();
    }

    [TestMethod]
    public void CreateNew_LocalUser_CreatesUser()
    {
        var factory = GetServices().GetRequiredService<IUser.CreateNew>();
        var u = factory(ValidEmail, null, ValidHash, UserRole.Admin);
        u.ExternalSubject.Should().BeNull();
        u.PasswordHash.Should().Be(ValidHash);
    }

    [TestMethod]
    public void CreateNew_NoSubjectAndNoHash_Throws()
    {
        var factory = GetServices().GetRequiredService<IUser.CreateNew>();
        var act = () => factory(ValidEmail, null, null, UserRole.Member);
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void CreateNew_EmptyEmail_Throws()
    {
        var factory = GetServices().GetRequiredService<IUser.CreateNew>();
        var act = () => factory(string.Empty, ValidSubject, null, UserRole.Member);
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task ChangePasswordHash_UpdatesHash()
    {
        var services = GetServices();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var u = await gen.CreateAsync(CancellationToken);

        var updated = await u.ChangePasswordHash("new-hash", CancellationToken);

        updated.PasswordHash.Should().Be("new-hash");
    }

    [TestMethod]
    public void CreateNew_WithoutLanguage_DefaultsToEnglish()
    {
        var factory = GetServices().GetRequiredService<IUser.CreateNew>();
        var u = factory(ValidEmail, ValidSubject, null, UserRole.Member);
        u.Language.Should().Be(SupportedLanguages.Default);
    }

    [TestMethod]
    public void CreateNew_WithSupportedLanguage_SetsLanguage()
    {
        var factory = GetServices().GetRequiredService<IUser.CreateNew>();
        var u = factory(ValidEmail, ValidSubject, null, UserRole.Member, "de");
        u.Language.Should().Be("de");
    }

    [TestMethod]
    public void CreateNew_UnsupportedLanguage_Throws()
    {
        var factory = GetServices().GetRequiredService<IUser.CreateNew>();
        var act = () => factory(ValidEmail, ValidSubject, null, UserRole.Member, "xx");
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task ChangeLanguage_UpdatesAndPersists()
    {
        var services = GetServices();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var u = await gen.CreateAsync(CancellationToken);

        await u.ChangeLanguage("de", CancellationToken);

        var reloaded = await services.GetRequiredService<IRepository<IUser>>().GetAsync(u.Id, CancellationToken);
        reloaded.Language.Should().Be("de");
    }

    [TestMethod]
    public async Task ChangeLanguage_ToUnsupported_Throws()
    {
        var services = GetServices();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var u = await gen.CreateAsync(CancellationToken);

        await FluentActions
            .Invoking(() => u.ChangeLanguage("xx", CancellationToken))
            .Should().ThrowAsync<Exception>();
    }
}
