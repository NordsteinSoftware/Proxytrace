using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class UserTests : DomainTest<Module>
{
    [TestMethod]
    public void CreateNew_DefaultsEmailPreferences_OnAndInfo()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<IUser.CreateNew>();

        var user = create("u@example.test", externalSubject: null, passwordHash: "hash", UserRole.Member);

        user.EmailNotificationsEnabled.Should().BeTrue();
        user.EmailNotificationMinSeverity.Should().Be(NotificationSeverity.Info);
    }

    [TestMethod]
    public async Task ChangeEmailNotificationPreferences_PersistsNewValues()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<IUser.CreateNew>();
        var repo = services.GetRequiredService<IRepository<IUser>>();
        var user = await create("u@example.test", externalSubject: null, passwordHash: "hash", UserRole.Member)
            .AddAsync(CancellationToken);

        await user.ChangeEmailNotificationPreferences(false, NotificationSeverity.Critical, CancellationToken);

        var reloaded = await repo.GetAsync(user.Id, CancellationToken);
        reloaded.EmailNotificationsEnabled.Should().BeFalse();
        reloaded.EmailNotificationMinSeverity.Should().Be(NotificationSeverity.Critical);
    }

    [TestMethod]
    public async Task ChangeEmailNotificationPreferences_WhenUnchanged_IsNoOp()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<IUser.CreateNew>();
        var user = await create("u@example.test", externalSubject: null, passwordHash: "hash", UserRole.Member)
            .AddAsync(CancellationToken);

        var same = await user.ChangeEmailNotificationPreferences(true, NotificationSeverity.Info, CancellationToken);

        same.Should().BeSameAs(user);
    }
}
