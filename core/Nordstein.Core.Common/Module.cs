using Autofac;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Conversion;
using Nordstein.Core.Common.Conversion.Internal;
using Nordstein.Core.Common.Hosting;
using Nordstein.Core.Common.Hosting.Internal;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Common.Lifecycle.Internal;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Common.Random.Internal;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.Common.Serialization.Internal;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Common.Time.Internal;

namespace Nordstein.Core.Common;

public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        
        builder
            .RegisterType<TypeConverter>()
            .As<ITypeConverter>()
            .SingleInstance();

        builder.RegisterType<SeededRandom>()
            .AsSelf();
        
        builder
            .Register(c => c.Resolve<SeededRandom.Factory>()(seed: 420))
            .As<IRandom>()
            .SingleInstance();

        builder.RegisterType<JsonSerializer>()
            .As<ISerializer>()
            .SingleInstance();

        builder.RegisterType<AsyncLock>().As<IAsyncLock>().SingleInstance();

        builder.RegisterType<SystemClock>().As<IClock>().SingleInstance();

        builder.RegisterType<AppVersion>().As<IAppVersion>().SingleInstance();

        builder.RegisterType<NullHostedService>().AsSelf();
        
        builder.RegisterType<TempDirectory>()
            .As<ITempDirectory>()
            .OwnedByLifetimeScope();
    }
}