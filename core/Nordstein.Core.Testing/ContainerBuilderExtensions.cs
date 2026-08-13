using Autofac;
using NSubstitute;

namespace Nordstein.Core.Testing;

public static class ContainerBuilderExtensions
{
    public static void RegisterStub<TService>(this ContainerBuilder builder, Action<TService>? config = null)
        where TService : class
    {
        builder.Register(_ =>
            {
                var fake = Substitute.For<TService>();
                config?.Invoke(fake);
                return fake;
            })
            .As<TService>()
            .InstancePerDependency();
    }
}