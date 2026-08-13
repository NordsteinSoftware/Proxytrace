using Autofac;

namespace Nordstein.Core.Common.Tests;

public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterModule<Common.Module>();
    }
}