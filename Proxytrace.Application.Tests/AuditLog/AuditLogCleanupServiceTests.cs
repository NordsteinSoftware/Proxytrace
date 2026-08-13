using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Proxytrace.Application.AuditLog;
using Proxytrace.Application.AuditLog.Internal;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.AuditLog;

[TestClass]
public sealed class AuditLogCleanupServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CleanOnce_RemovesEntriesOlderThanRetention_KeepsNewer()
    {
        var services = GetServices(builder =>
            builder.RegisterInstance(new AuditLogCleanupConfiguration
            {
                RetentionDurationDays = 365,
                CleanupIntervalHours = 24,
            }));

        var generator = services.GetRequiredService<IAuditLogEntryGenerator>();
        var repository = services.GetRequiredService<IAuditLogRepository>();
        var service = services.GetRequiredService<AuditLogCleanupService>();

        // Two entries older than the 365-day retention — removed (exercises the in-memory provider's
        // RemoveOlderThan fallback path, since ExecuteDelete is unsupported there).
        await generator.CreateAsync(DateTimeOffset.UtcNow.AddDays(-400), CancellationToken);
        await generator.CreateAsync(DateTimeOffset.UtcNow.AddDays(-366), CancellationToken);

        // Two recent entries within retention — kept.
        await generator.CreateAsync(DateTimeOffset.UtcNow.AddDays(-1), CancellationToken);
        await generator.CreateAsync(DateTimeOffset.UtcNow.AddMinutes(-10), CancellationToken);

        await service.CleanOnceAsync(CancellationToken);

        (await repository.CountAsync(CancellationToken)).Should().Be(2);
    }

    [TestMethod]
    public void Constructor_WithNonPositiveRetention_Throws()
    {
        var services = GetServices(builder =>
            builder.RegisterInstance(new AuditLogCleanupConfiguration
            {
                RetentionDurationDays = 0,
            }));

        FluentActions.Invoking(services.GetRequiredService<AuditLogCleanupService>)
            .Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CleanOnce_WhenRepositoryThrows_DoesNotPropagate()
    {
        var repository = Substitute.For<IAuditLogRepository>();
        repository.RemoveOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("boom"));

        var service = new AuditLogCleanupService(
            new AuditLogCleanupConfiguration { RetentionDurationDays = 365 },
            NullLogger<AuditLogCleanupService>.Instance,
            repository);

        await FluentActions.Invoking(() => service.CleanOnceAsync(CancellationToken)).Should().NotThrowAsync();
    }
}
