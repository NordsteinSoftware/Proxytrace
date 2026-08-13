using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Statistics.Internal;
using Proxytrace.Application.Statistics.Internal.Worker;
using Proxytrace.Application.Statistics.TestRun.Internal;
using Nordstein.Core.Common.DependencyInjection;

namespace Proxytrace.Application.Statistics;

internal class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterType<TestRunStatsProjector>()
            .As<IStatsProjector>()
            .SingleInstance();

        // Default dashboard cache tuning so Application-layer consumers resolve even when no host
        // binds the "Statistics" section. The API registers the config-bound options later, which
        // wins. Mirrors the AuthOptions default in the parent module.
        builder.Register(_ => new DashboardCacheOptions())
            .As<DashboardCacheOptions>()
            .SingleInstance()
            .IfNotRegistered(typeof(DashboardCacheOptions));

        builder.RegisterType<DashboardStatistics>()
            .As<IDashboardStatistics>()
            .SingleInstance();

        builder.RegisterType<AgentStatistics>()
            .As<IAgentStatistics>()
            .SingleInstance();

        builder.RegisterType<StatisticsHostedService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<StatisticsHostedService>()));

        builder.RegisterType<StatisticsBackfillHostedService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<StatisticsBackfillHostedService>()));
    }
}
