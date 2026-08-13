using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.User;
using Proxytrace.Domain.UserTotpEnrollment;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class UserTotpEnrollmentValidationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidArgs_CreatesPendingEnrollment()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IUserTotpEnrollment.CreateNew>();

        var enrollment = factory(user, "JBSWY3DPEHPK3PXP");

        enrollment.User.Id.Should().Be(user.Id);
        enrollment.Secret.Should().Be("JBSWY3DPEHPK3PXP");
        enrollment.ConfirmedAt.Should().BeNull();
        enrollment.IsConfirmed.Should().BeFalse();
        enrollment.LastUsedStep.Should().BeNull();
        enrollment.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task CreateNew_WithEmptySecret_Throws()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IUserTotpEnrollment.CreateNew>();

        var act = () => factory(user, "");
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_CalledTwice_ProducesDistinctIds()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IUserTotpEnrollment.CreateNew>();

        var a = factory(user, "JBSWY3DPEHPK3PXP");
        var b = factory(user, "JBSWY3DPEHPK3PXP");

        a.Id.Should().NotBe(b.Id);
    }

    [TestMethod]
    public async Task Confirm_SetsConfirmedAtAndStepAndPersists()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IUserTotpEnrollment.CreateNew>();
        var enrollment = await factory(user, "JBSWY3DPEHPK3PXP").AddAsync(CancellationToken);

        var confirmed = await enrollment.Confirm(42, CancellationToken);

        confirmed.ConfirmedAt.Should().NotBeNull();
        confirmed.IsConfirmed.Should().BeTrue();
        confirmed.LastUsedStep.Should().Be(42);

        var reloaded = await services.GetRequiredService<IRepository<IUserTotpEnrollment>>().GetAsync(enrollment.Id, CancellationToken);
        reloaded.ConfirmedAt.Should().NotBeNull();
        reloaded.LastUsedStep.Should().Be(42);
    }

    [TestMethod]
    public async Task RecordUsedStep_UpdatesStepAndPersists()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IUserTotpEnrollment.CreateNew>();
        var enrollment = await factory(user, "JBSWY3DPEHPK3PXP").AddAsync(CancellationToken);

        var updated = await enrollment.RecordUsedStep(99, CancellationToken);

        updated.LastUsedStep.Should().Be(99);
        var reloaded = await services.GetRequiredService<IRepository<IUserTotpEnrollment>>().GetAsync(enrollment.Id, CancellationToken);
        reloaded.LastUsedStep.Should().Be(99);
    }
}
