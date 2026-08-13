using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Search.Internal;
using Proxytrace.Application.Search.Internal.Mappers;
using Nordstein.Core.Common.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search;

internal sealed class SearchModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder.RegisterInstance(new SearchConfiguration())
            .AsSelf()
            .SingleInstance()
            .IfNotRegistered(typeof(SearchConfiguration));

        builder.Register(ctx =>
            {
                var cfg = ctx.Resolve<SearchConfiguration>();
                ctx.TryResolve<IHostEnvironment>(out var env);
                ctx.TryResolve<ILogger<LuceneDirectoryFactory>>(out var log);
                return new LuceneDirectoryFactory(cfg, env, log);
            })
            .As<ILuceneDirectoryFactory>()
            .SingleInstance();

        builder.RegisterType<LuceneIndexWriter>()
            .AsSelf()
            .SingleInstance();

        foreach (var mapperType in typeof(IDocumentMapper).GetImplementations())
        {
            builder.RegisterType(mapperType).As<IDocumentMapper>().InstancePerLifetimeScope();
        }

        builder.RegisterType<LuceneIndexingService>()
            .AsSelf()
            .As<ISearchIndexer>()
            .SingleInstance();
        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<LuceneIndexingService>()));

        builder.RegisterType<LuceneSearchService>()
            .As<ISearchService>()
            .SingleInstance();

        builder.RegisterType<ProjectSearchSettingsResolver>()
            .As<IProjectSearchSettingsResolver>()
            .SingleInstance();

        builder.RegisterType<ReindexStateTracker>()
            .As<IReindexStateTracker>()
            .SingleInstance();

        builder.RegisterType<LuceneSearchIndexStatistics>()
            .As<ISearchIndexStatistics>()
            .SingleInstance();

        // Keep the index in sync off the entity-change event stream — the one seam every
        // repository write flows through (AbstractRepository.Notify), regardless of which
        // interface the caller used. A repository decorator can't observe writes made through a
        // custom repo interface (e.g. IAgentCallRepository), so it missed every searchable kind.
        builder.RegisterType<EntityChangeIndexingService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<EntityChangeIndexingService>()));

        builder.RegisterType<TraceIndexPrunerService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterServiceCollection(services =>
            services.AddHostedService(sc => sc.GetRequiredService<TraceIndexPrunerService>()));
    }
}
