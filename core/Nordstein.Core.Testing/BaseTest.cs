using Autofac;
using JetBrains.Annotations;

namespace Nordstein.Core.Testing;

[TestClass]
public abstract class BaseTest<TModule> where TModule : Autofac.Module, new()
{
    public required TestContext TestContext { get; [UsedImplicitly] init; }

    protected CancellationToken CancellationToken
        => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        TestContext.Properties["Containers"] = new List<IContainer>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (IContainer container in GetTestContainers())
        {
            container.Dispose();
        }
    }

    protected virtual void ConfigureContainer(ContainerBuilder builder)
    {
    }

    protected IServiceProvider GetServices(Action<ContainerBuilder>? action = null)
    {
        IContainer container = BuildContainer(builder =>
        {
            ConfigureContainer(builder);
            action?.Invoke(builder);
        });

        // register container in test context so it is disposed in Cleanup
        var containers = GetTestContainers();
        containers.Add(container);

        return container.Resolve<IServiceProvider>();
    }

    /// <summary>
    /// Builds a DI container without registering it for per-test cleanup. Useful from a
    /// static <c>[ClassInitialize]</c> to share an expensive fixture (e.g. seeded data)
    /// across the tests of a class; the caller owns disposal.
    /// </summary>
    protected static IContainer BuildContainer(Action<ContainerBuilder>? action = null)
    {
        ContainerBuilder builder = new ContainerBuilder();
        builder.RegisterModule<Module>();
        builder.RegisterModule<TModule>();
        action?.Invoke(builder);
        return builder.Build();
    }

    private List<IContainer> GetTestContainers()
    {
        if (TestContext.Properties.TryGetValue("Containers", out object? containersObj) &&
            containersObj is List<IContainer> containers)
        {
            return containers;
        }

        throw new InvalidOperationException(
            "TestContext does not contain a list of containers. Ensure that the TestInitialize method is properly setting up the list.");
    }
}