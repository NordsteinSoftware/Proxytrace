using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Auth;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth;

[TestClass]
public sealed class JitUserProvisionerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task EnsureProvisioned_FirstUser_IsCreatedAsAdmin()
    {
        IServiceProvider services = GetServices();
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();

        var user = await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);

        user.Role.Should().Be(UserRole.Admin);
        user.Email.Should().Be("first@example.com");
    }

    [TestMethod]
    public async Task EnsureProvisioned_SecondUser_IsCreatedAsMember()
    {
        IServiceProvider services = GetServices();
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();

        await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);
        var second = await provisioner.EnsureProvisionedAsync("ext-002", "second@example.com", CancellationToken);

        second.Role.Should().Be(UserRole.Member);
    }

    [TestMethod]
    public async Task EnsureProvisioned_SameSubjectTwice_ReturnsSameUser()
    {
        IServiceProvider services = GetServices();
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();

        var first = await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);
        var second = await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);

        second.Id.Should().Be(first.Id);
    }

    [TestMethod]
    public async Task EnsureProvisioned_SameSubject_IgnoresChangedEmailOnSubsequent()
    {
        IServiceProvider services = GetServices();
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();

        var first = await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);
        var second = await provisioner.EnsureProvisionedAsync("ext-001", "renamed@example.com", CancellationToken);

        second.Id.Should().Be(first.Id);
        second.Email.Should().Be("first@example.com");
    }

    [TestMethod]
    public async Task EnsureProvisioned_TwoSubjects_BothPersisted()
    {
        IServiceProvider services = GetServices();
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();
        var repo = services.GetRequiredService<IUserRepository>();

        await provisioner.EnsureProvisionedAsync("ext-001", "a@example.com", CancellationToken);
        await provisioner.EnsureProvisionedAsync("ext-002", "b@example.com", CancellationToken);

        var count = await repo.CountAsync(CancellationToken);
        count.Should().Be(2);
    }

    [TestMethod]
    public async Task EnsureProvisioned_FirstUser_AuditsAdminBootstrapped()
    {
        var audit = new RecordingAuditLogger();
        IServiceProvider services = GetServices(b => b.RegisterInstance(audit).As<ILogger<Audit>>());
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();

        await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);

        audit.Events.Should().ContainSingle().Which.Id.Should().Be((int)AuditAction.AdminBootstrapped);
    }

    [TestMethod]
    public async Task EnsureProvisioned_SecondUser_AuditsUserSignedUp()
    {
        var audit = new RecordingAuditLogger();
        IServiceProvider services = GetServices(b => b.RegisterInstance(audit).As<ILogger<Audit>>());
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();

        await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);
        await provisioner.EnsureProvisionedAsync("ext-002", "second@example.com", CancellationToken);

        audit.Events.Should().HaveCount(2);
        audit.Events[1].Id.Should().Be((int)AuditAction.UserSignedUp);
    }

    [TestMethod]
    public async Task EnsureProvisioned_ExistingUser_RecordsNothing()
    {
        var audit = new RecordingAuditLogger();
        IServiceProvider services = GetServices(b => b.RegisterInstance(audit).As<ILogger<Audit>>());
        var provisioner = services.GetRequiredService<IJitUserProvisioner>();

        await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);
        await provisioner.EnsureProvisionedAsync("ext-001", "first@example.com", CancellationToken);

        // Only the initial provisioning is audited; a returning user's sign-in is not re-recorded.
        audit.Events.Should().ContainSingle();
    }

    private sealed class RecordingAuditLogger : ILogger<Audit>
    {
        public List<EventId> Events { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Events.Add(eventId);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
