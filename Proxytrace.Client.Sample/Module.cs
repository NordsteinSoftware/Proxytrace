using Autofac;
using Microsoft.Extensions.Configuration;
using Proxytrace.Client.Sample.Internal;

namespace Proxytrace.Client.Sample;

/// <summary>
/// Autofac module that registers  services.
/// </summary>
public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

         builder.RegisterType<AgentCallSimulator>()
             .AsSelf()
             .InstancePerDependency();

         builder.RegisterType<ToolCallSimulator>()
             .AsSelf()
             .InstancePerDependency();

         builder.Register(_ =>
         {
             ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
             return configurationBuilder
                 .AddJsonFile("appsettings.development.json", optional: false, reloadOnChange: true)
                 .Build();
         }).As<IConfiguration>();
         
         builder
             .Register(cp => cp.Resolve<IConfiguration>().Get<Configuration>() 
                             ?? throw new InvalidOperationException("Failed to bind configuration."))
             .As<Configuration>()
             .SingleInstance();
    }
}
