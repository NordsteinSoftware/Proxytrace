using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Proxytrace.Application.ErrorLog;
using Proxytrace.Application.ErrorLog.Internal;
using Proxytrace.Domain.ApplicationError;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.ErrorLog;

[TestClass]
public sealed class ErrorLogCleanupServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CleanOnce_RemovesAgedRowsAndTrimsToCap()
    {
        var services = GetServices(builder =>
            builder.RegisterInstance(new ErrorLogCleanupConfiguration
            {
                RetentionDurationDays = 2,
                CleanupIntervalHours = 1,
                MaxRetained = 3,
            }));

        var generator = services.GetRequiredService<IApplicationErrorGenerator>();
        var repository = services.GetRequiredService<IApplicationErrorRepository>();
        var service = services.GetRequiredService<ErrorLogCleanupService>();

        // Two rows older than the 2-day retention — removed by age.
        await generator.CreateAsync(DateTimeOffset.UtcNow.AddDays(-5), CancellationToken);
        await generator.CreateAsync(DateTimeOffset.UtcNow.AddDays(-4), CancellationToken);

        // Five fresh rows within retention — trimmed down to the cap of 3.
        for (var i = 0; i < 5; i++)
        {
            await generator.CreateAsync(DateTimeOffset.UtcNow.AddMinutes(-(i + 1) * 10), CancellationToken);
        }

        await service.CleanOnceAsync(CancellationToken);

        (await repository.CountAsync(CancellationToken)).Should().Be(3);
    }

    [TestMethod]
    public void Constructor_WithNonPositiveRetention_Throws()
    {
        var services = GetServices(builder =>
            builder.RegisterInstance(new ErrorLogCleanupConfiguration
            {
                RetentionDurationDays = -1,
                MaxRetained = 10,
            }));

        FluentActions.Invoking(services.GetRequiredService<ErrorLogCleanupService>)
            .Should().Throw<Exception>();
    }

    [TestMethod]
    public void Constructor_WithNonPositiveMaxRetained_Throws()
    {
        var services = GetServices(builder =>
            builder.RegisterInstance(new ErrorLogCleanupConfiguration
            {
                RetentionDurationDays = 14,
                MaxRetained = 0,
            }));

        FluentActions.Invoking(services.GetRequiredService<ErrorLogCleanupService>)
            .Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CleanOnce_WhenRepositoryThrows_DoesNotPropagate()
    {
        var repository = Substitute.For<IApplicationErrorRepository>();
        repository.RemoveOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("boom"));

        var services = GetServices(builder =>
        {
            builder.RegisterInstance(repository).As<IApplicationErrorRepository>();
            builder.RegisterInstance(new ErrorLogCleanupConfiguration
            {
                RetentionDurationDays = 14,
                MaxRetained = 10,
            });
        });

        var service = services.GetRequiredService<ErrorLogCleanupService>();

        await FluentActions.Invoking(() => service.CleanOnceAsync(CancellationToken)).Should().NotThrowAsync();
    }
}
