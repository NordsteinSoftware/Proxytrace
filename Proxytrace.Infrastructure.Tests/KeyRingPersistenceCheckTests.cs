using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Proxytrace.Infrastructure.Security;
using Proxytrace.Testing;

namespace Proxytrace.Infrastructure.Tests;

/// <summary>
/// A missing PROXYTRACE_DATA_DIR leaves the Data Protection key ring in memory, so every secret
/// encrypted at rest stops decrypting after a restart — and the three decrypt paths swallow the
/// resulting CryptographicException at Warning only. The operator Error Log captures >= Error, so
/// without a Critical at startup that outage is completely invisible. See docs/security.md.
/// </summary>
[TestClass]
public sealed class KeyRingPersistenceCheckTests : BaseTest<Module>
{
    [TestMethod]
    public async Task StartAsync_WhenNoDataDirectoryIsConfigured_LogsCritical()
    {
        var logger = Substitute.For<ILogger<SecretProtectionModule>>();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterModule<SecretProtectionModule>();
            builder.RegisterInstance(new KeyRingLocation(null));
            builder.RegisterInstance(logger).As<ILogger<SecretProtectionModule>>();
        });

        await services.GetRequiredService<KeyRingPersistenceCheck>().StartAsync(CancellationToken);

        logger.Received(1).Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [TestMethod]
    public async Task StartAsync_WhenTheKeyRingIsPersisted_DoesNotLogCritical()
    {
        var logger = Substitute.For<ILogger<SecretProtectionModule>>();
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterModule<SecretProtectionModule>();
            builder.RegisterInstance(new KeyRingLocation("/app/data/dataprotection-keys"));
            builder.RegisterInstance(logger).As<ILogger<SecretProtectionModule>>();
        });

        await services.GetRequiredService<KeyRingPersistenceCheck>().StartAsync(CancellationToken);

        logger.DidNotReceive().Log(
            LogLevel.Critical,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }
}
