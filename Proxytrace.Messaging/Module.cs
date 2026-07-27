using Autofac;
using Proxytrace.Messaging.Internal;
using StackExchange.Redis;

namespace Proxytrace.Messaging;

/// <summary>
/// Registers the configured <see cref="IIngestionStream"/> implementation. Hosts pass a
/// <see cref="MessagingConfiguration"/>; the default (no argument) selects the in-process stream
/// used by tests and single-process runs.
/// </summary>
public sealed class Module : Autofac.Module
{
    private readonly MessagingConfiguration configuration;

    public Module(MessagingConfiguration? configuration = null)
        => this.configuration = configuration ?? new MessagingConfiguration();

    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterInstance(configuration).SingleInstance();

        if (configuration.Provider == MessagingProvider.Redis)
        {
            builder.Register<IConnectionMultiplexer>(_ =>
                {
                    var options = ConfigurationOptions.Parse(configuration.RedisConnectionString);
                    // Never throw on connect: a Redis outage must not stop the proxy from
                    // constructing its controller and forwarding agent traffic. The multiplexer
                    // reconnects in the background. It does NOT fail fast while down — a command is
                    // backlogged until its async timeout (~5s) — so RedisIngestionStream checks
                    // IConnectionMultiplexer.IsConnected before issuing one and drops the capture
                    // instead, keeping the proxy hot path free of that stall.
                    options.AbortOnConnectFail = false;
                    return ConnectionMultiplexer.Connect(options);
                })
                .SingleInstance();

            builder.RegisterType<RedisIngestionStream>()
                .As<IIngestionStream>()
                .SingleInstance();
        }
        else
        {
            builder.RegisterType<InProcessIngestionStream>()
                .As<IIngestionStream>()
                .SingleInstance();
        }
    }
}
