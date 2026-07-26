using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Common.DependencyInjection;

namespace Proxytrace.Common.Tests;

[TestClass]
public sealed class AutofacExtensionsTests
{
    private interface IPlumbing;

    private sealed class SharedPlumbing : IPlumbing;

    private sealed class OtherPlumbing : IPlumbing;

    [TestMethod]
    public void RegisterServiceCollection_WhenTwoModulesRegisterTheSameImplementation_PopulatesItOnce()
    {
        // Framework extension methods share their plumbing through TryAddEnumerable, which dedupes
        // only within one IServiceCollection. Every RegisterServiceCollection call builds a fresh
        // one, so without this the container ends up with a copy per caller — which is how a single
        // upstream request came to be logged once per module that had called AddHttpClient (#451).
        var builder = new ContainerBuilder();
        builder.RegisterServiceCollection(services =>
            services.AddSingleton<IPlumbing, SharedPlumbing>());
        builder.RegisterServiceCollection(services =>
            services.AddSingleton<IPlumbing, SharedPlumbing>());

        using var container = builder.Build();

        container.Resolve<IEnumerable<IPlumbing>>().Should().ContainSingle()
            .Which.Should().BeOfType<SharedPlumbing>();
    }

    [TestMethod]
    public void RegisterServiceCollection_WithDistinctImplementations_KeepsEveryOne()
    {
        // Registering several implementations of one service is the whole point of IEnumerable<T>
        // resolution — only an identical (service, implementation, lifetime) triple is a duplicate.
        var builder = new ContainerBuilder();
        builder.RegisterServiceCollection(services =>
            services.AddSingleton<IPlumbing, SharedPlumbing>());
        builder.RegisterServiceCollection(services =>
            services.AddSingleton<IPlumbing, OtherPlumbing>());

        using var container = builder.Build();

        container.Resolve<IEnumerable<IPlumbing>>()
            .Should().HaveCount(2)
            .And.ContainItemsAssignableTo<IPlumbing>();
    }

    [TestMethod]
    public void RegisterServiceCollection_WithInstanceRegistrations_LeavesThemAlone()
    {
        // Instance and factory descriptors are opaque — two of them are never provably the same
        // registration, so they are populated exactly as written.
        var first = new SharedPlumbing();
        var second = new SharedPlumbing();

        var builder = new ContainerBuilder();
        builder.RegisterServiceCollection(services => services.AddSingleton<IPlumbing>(first));
        builder.RegisterServiceCollection(services => services.AddSingleton<IPlumbing>(second));

        using var container = builder.Build();

        container.Resolve<IEnumerable<IPlumbing>>().Should().HaveCount(2);
    }
}
