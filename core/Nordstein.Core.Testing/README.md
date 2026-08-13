# Nordstein.Core.Testing

Shared MSTest harness for Nordstein applications.

## `BaseTest<TModule>`

Derive from it and every test gets a fresh Autofac container, built from
`Nordstein.Core.Testing.Module` plus the module under test, and disposed automatically:

```csharp
[TestClass]
public sealed class ThingTests : BaseTest<MyProduct.Application.Module>
{
    [TestMethod]
    public async Task DoesTheThing()
    {
        IServiceProvider services = GetServices();
        var subject = services.GetRequiredService<IThing>();

        (await subject.DoAsync(CancellationToken)).Should().BeTrue();
    }
}
```

- `GetServices(Action<ContainerBuilder>?)` builds a container registered for per-test cleanup.
- `BuildContainer(...)` is the static escape hatch for a `[ClassInitialize]` fixture shared
  across a class; the caller owns disposal.
- `ConfigureContainer` is the per-class override point for substitutes.
- `CancellationToken` comes from the `TestContext`, so a test that hangs is cancelled by the
  runner rather than running to the timeout.

The package brings AwesomeAssertions and NSubstitute along transitively — a consuming test
project does not reference them itself.
