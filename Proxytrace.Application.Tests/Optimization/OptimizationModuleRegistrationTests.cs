using Autofac;
using Autofac.Core;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Proxytrace.Application.Optimization.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Optimization;

/// <summary>
/// Guards the DI registration shape of the optimization hosted services.
///
/// Regression for a double-start bug: both <see cref="OptimizerService"/> and
/// <see cref="TheoryValidationService"/> derive from <c>BackgroundService</c> (which
/// implements <see cref="IHostedService"/>). The module registered them with
/// <c>.AsImplementedInterfaces()</c> AND a separate <c>AddHostedService(...)</c>, so the
/// generic host saw the same singleton as <see cref="IHostedService"/> twice and called
/// <c>StartAsync</c>/<c>ExecuteAsync</c> twice — causing every optimization theory to be
/// created twice. The fix removes <c>.AsImplementedInterfaces()</c>; the explicit
/// <c>AddHostedService</c> must be the ONLY source of the <see cref="IHostedService"/>
/// exposure for these types.
/// </summary>
[TestClass]
public sealed class OptimizationModuleRegistrationTests : BaseTest<Module>
{
    [TestMethod]
    public void OptimizationModule_OptimizerService_NotExposedAsHostedServiceByConcreteRegistration()
    {
        IServiceProvider services = GetServices(builder =>
            builder.RegisterModule<Proxytrace.Application.Optimization.Module>());

        ConcreteTypeExposesHostedService(services, typeof(OptimizerService))
            .Should().BeFalse(
                "the IHostedService registration must come only from AddHostedService, " +
                "otherwise ExecuteAsync runs twice and theories are created twice");
    }

    [TestMethod]
    public void OptimizationModule_TheoryValidationService_NotExposedAsHostedServiceByConcreteRegistration()
    {
        IServiceProvider services = GetServices(builder =>
            builder.RegisterModule<Proxytrace.Application.Optimization.Module>());

        ConcreteTypeExposesHostedService(services, typeof(TheoryValidationService))
            .Should().BeFalse(
                "the IHostedService registration must come only from AddHostedService, " +
                "otherwise ExecuteAsync runs twice and theories are created twice");
    }

    /// <summary>
    /// True when some component whose concrete (limit) type is <paramref name="concreteType"/>
    /// also advertises <see cref="IHostedService"/> as a service — i.e. the
    /// <c>.AsImplementedInterfaces()</c> re-exposure that the explicit
    /// <c>AddHostedService(...)</c> already provides.
    /// </summary>
    private static bool ConcreteTypeExposesHostedService(IServiceProvider services, Type concreteType)
    {
        ILifetimeScope scope = services.GetRequiredService<ILifetimeScope>();

        return scope.ComponentRegistry.Registrations.Any(registration =>
            registration.Activator.LimitType == concreteType &&
            registration.Services
                .OfType<TypedService>()
                .Any(service => service.ServiceType == typeof(IHostedService)));
    }
}
