using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nordstein.Core.Common.Hosting;

namespace Nordstein.Core.Common.Tests.Hosting;

/// <summary>
/// Covers the host-level guard from #522: an unhandled exception in a background loop must not
/// stop the process. These build a real <see cref="IHost"/> rather than the shared test container,
/// because the behaviour under test *is* the host's own fault handling.
/// </summary>
[TestClass]
public sealed class HostingServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddResilientBackgroundServices_ConfiguresIgnoreBehavior()
    {
        var services = new ServiceCollection();

        services.AddResilientBackgroundServices();

        HostOptions options = services.BuildServiceProvider().GetRequiredService<IOptions<HostOptions>>().Value;
        options.BackgroundServiceExceptionBehavior.Should().Be(BackgroundServiceExceptionBehavior.Ignore);
    }

    [TestMethod]
    public async Task FaultedBackgroundService_WithResilientHosting_LeavesTheHostRunning()
    {
        using IHost host = BuildHost(configure: builder => builder.AddResilientBackgroundServices());

        await host.StartAsync(CancellationToken.None);
        await WaitForFaultAsync(host);

        // Still up: the faulted loop is gone, the process (and with it the HTTP surface) is not.
        HostRunning(host).Should().BeTrue();

        await host.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The regression this guards against: on the framework default the same fault stops the host,
    /// which is what produced the silent exit-0 API container in #522.
    /// </summary>
    [TestMethod]
    public async Task FaultedBackgroundService_OnTheFrameworkDefault_StopsTheHost()
    {
        using IHost host = BuildHost(configure: _ => { });

        await host.StartAsync(CancellationToken.None);
        await WaitForFaultAsync(host);

        HostRunning(host).Should().BeFalse();
    }

    private static IHost BuildHost(Action<IServiceCollection> configure) =>
        new HostBuilder()
            .ConfigureServices(services =>
            {
                configure(services);
                services.AddSingleton<ThrowingBackgroundService>();
                services.AddHostedService(sp => sp.GetRequiredService<ThrowingBackgroundService>());
            })
            .Build();

    /// <summary>
    /// Waits until the background service has actually thrown, then gives the host a moment to act
    /// on it — the stop is asynchronous, so asserting immediately would race the shutdown.
    /// </summary>
    private static async Task WaitForFaultAsync(IHost host)
    {
        await host.Services.GetRequiredService<ThrowingBackgroundService>().Faulted;
        await Task.Delay(TimeSpan.FromMilliseconds(200));
    }

    /// <summary>
    /// The host's own view of whether it is still up: <c>StopHost</c> triggers the application
    /// lifetime's stopping token, <c>Ignore</c> leaves it untouched.
    /// </summary>
    private static bool HostRunning(IHost host) =>
        !host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested;

    private sealed class ThrowingBackgroundService : BackgroundService
    {
        private readonly TaskCompletionSource faulted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Faulted => faulted.Task;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Yield first: a synchronous throw completes inside StartAsync and would surface as a
            // startup failure instead of the mid-run fault this covers.
            await Task.Yield();
            faulted.SetResult();
            throw new InvalidOperationException("background loop failed");
        }
    }
}
