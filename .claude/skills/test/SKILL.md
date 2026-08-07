# Writing Tests for Proxytrace

Use this guide whenever writing new backend tests or reviewing existing ones. It covers
every layer of the stack and the conventions that keep the suite parallel-safe and
maintainable.

---

## Core principles (read first)

These are non-negotiable and override any habit you bring from other codebases:

1. **No shared state between tests.** Do not store the system-under-test, fakes, fixtures,
   or seeded entities in instance fields, static fields, or `TestContext.Properties`.
   Everything a test needs is built *inside* the test method from a fresh service provider.
2. **No `[TestFixture]`-style helper classes, no shared setup objects, no builders that
   live outside the container.** If a test needs a dependency configured, configure it
   through **DI + NSubstitute**, not through hand-rolled fixture plumbing.
3. **The DI container is the fixture.** A fresh, isolated container (with its own in-memory
   database) is created per `GetServices()` call. Resolve what you need, assert, let
   `[TestCleanup]` dispose it. This is how you get isolation *and* avoid fixture classes at
   the same time.
4. **Substitute infrastructure, never the domain.** Fake out `IModelClient`,
   `IProviderClient`, `IHttpClientFactory`, external repos, etc. with NSubstitute. Use the
   real domain entities, real repositories, and real in-memory storage.

If you find yourself writing `private readonly Foo _foo = Substitute.For<Foo>();` at class
scope — stop. That is the anti-pattern this guide exists to prevent. Register the
substitute in the container instead (see *Injecting fakes* below).

> Note: a couple of existing integration tests (e.g.
> `UpdateSystemPromptOptimizerIntegrationTests`) still use instance-field substitutes.
> That is **debt, not precedent** — do not copy it.

---

## Test project layout

| Project | What it tests | Base class |
|---|---|---|
| `Proxytrace.Domain.Tests` | Domain entity construction, validation, state-machine transitions | `DomainTest<Module>` (extends `BaseTest<Module>`) |
| `Proxytrace.Storage.Tests` | Repository persistence and round-trip mapping via EF Core (in-memory) | `BaseTest<Module>` |
| `Proxytrace.Application.Tests` | Application services end-to-end (e.g. `TestRunnerService`) with faked infrastructure | `BaseTest<Module>` |
| `Proxytrace.Api.Tests` | HTTP controllers / routing | `BaseTest<Module>` |
| `Proxytrace.Infrastructure.Tests` | `ModelClient` and external integration wrappers | `BaseTest<Module>` |
| `Proxytrace.Serialization.Tests`, `Proxytrace.Common.Tests`, `Proxytrace.Proxy.Tests`, `Proxytrace.Messaging.Tests`, `Proxytrace.Licensing.Tests` | Their respective layers | `BaseTest<Module>` |

Each test project ships **one `Module : Autofac.Module`** that wires the layer under test
plus in-memory storage and the standard infrastructure stubs. This per-project module *is*
the shared baseline — there are no other shared fixtures. See *The per-project test module*.

---

## The base test harness

All tests extend `BaseTest<TModule>` from `Proxytrace.Testing`:

```csharp
[TestClass]
public sealed class MyTests : BaseTest<Module>   // or DomainTest<Module>
{
    // Class-wide DI configuration (optional). Applies to every GetServices() call
    // in this class. Use sparingly — prefer per-method configuration.
    protected override void ConfigureContainer(ContainerBuilder builder)
    {
        base.ConfigureContainer(builder);
        // ...
    }

    [TestMethod]
    public async Task Something_DoesX()
    {
        // Per-method DI configuration (optional). Local to this one test.
        IServiceProvider services = GetServices(builder =>
        {
            // ...
        });

        // resolve, act, assert
    }
}
```

### How the harness works (and why it's stateless)

- `GetServices(action)` builds a **brand-new Autofac container every call**: it registers
  `Proxytrace.Testing.Module`, then your `TModule`, then runs `ConfigureContainer`, then
  your per-call `action`. Each container has its **own isolated in-memory database**.
- The container is recorded in `TestContext.Properties["Containers"]` and disposed in
  `[TestCleanup]`. You never manage container lifetime yourself.
- `CancellationToken` is a protected property sourced from `TestContext.CancellationToken`.
  Pass it to every async call.
- MSTest constructs a **new test-class instance per test method**, and each test builds its
  own container — so there is *no* state to leak between tests as long as you keep
  everything inside the method. Don't reintroduce sharing through fields.

### The two configuration hooks

| Hook | Scope | Use it for |
|---|---|---|
| `ConfigureContainer(ContainerBuilder)` | Every test in the class | A substitution the *whole class* needs (rare). |
| `GetServices(builder => …)` | One test method | The default. Test-specific fakes and stub behavior. |

Prefer the per-method hook. Reach for `ConfigureContainer` only when literally every test
in the class needs the same override, and even then keep it minimal.

---

## The per-project test module

The layer baseline lives in each project's `Module`. This is where in-memory storage and
the always-needed infrastructure stubs are registered once, so individual tests stay clean.
Example (`Proxytrace.Application.Tests/Module.cs`, abridged):

```csharp
public class Module : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);
        builder.RegisterModule<Domain.Module>();
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.InMemory()));

        builder.RegisterStub<IModelClient>();      // NSubstitute fake, no behavior
        builder.RegisterStub<IProviderClient>();
        builder.RegisterStub<IAgentNameGenerator>(stub =>
            stub.GenerateNameAsync(Arg.Any<IPromptTemplate>(), Arg.Any<IProject>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(Task.FromResult("Test Agent")));
    }
}
```

If a stub is needed by *every* test in the project, register it here. If it is needed by
one class or one test, register it in `ConfigureContainer` / `GetServices` instead.

---

## DI + NSubstitute: the substitution toolkit

### `RegisterStub<T>` — quick fakes

`ContainerBuilderExtensions.RegisterStub<T>(config)` registers an NSubstitute fake:

```csharp
builder.RegisterStub<IModelClient>();                       // empty fake
builder.RegisterStub<IModelClient>(fake =>
    fake.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<CancellationToken>())
        .Returns(Task.FromResult(expected)));               // fake with behavior
```

> ⚠️ **Scope gotcha.** `RegisterStub<T>` uses `InstancePerDependency` — every resolve
> returns a **different** fake. That is fine for a pure collaborator you only inject. But if
> you need to **assert** on the same fake the SUT used (e.g. `Received()` calls), an
> InstancePerDependency stub will hand you a *different* instance and your assertion will
> silently see nothing. For assert-back fakes, register a single instance instead — see
> below.

### `RegisterInstance` — when you must configure *and* assert on the same fake

Create the fake once, register that exact instance, then resolve it back to assert:

```csharp
[TestMethod]
public async Task Ingest_PersistsCall_AndNotifiesBroadcaster()
{
    var broadcaster = Substitute.For<ITraceBroadcaster>();

    IServiceProvider services = GetServices(builder =>
        builder.RegisterInstance(broadcaster).As<ITraceBroadcaster>());

    var ingestor = services.GetRequiredService<IAgentCallIngestor>();
    await ingestor.IngestAsync(payload, CancellationToken);

    // same instance the SUT received — assertion is meaningful
    await broadcaster.Received(1).BroadcastAsync(Arg.Any<IAgentCall>(), CancellationToken);
}
```

This keeps the fake **local to the test** (no field, no fixture) while still letting you
both stub and verify it. This is the preferred pattern over instance fields.

### Faking repositories vs. using real storage

Default to **real in-memory storage** — it is registered by the per-project module and
exercises the real mappers. Only substitute `IRepository<T>` when you specifically want to
test failure handling or isolate the SUT from persistence:

```csharp
var repo = Substitute.For<IRepository<IAgentCall>>();
repo.AddAsync(Arg.Any<IAgentCall>(), Arg.Any<CancellationToken>())
    .Returns(call =>
    {
        var added = call.Arg<IAgentCall>();
        ArgumentNullException.ThrowIfNull(added);   // see nullability note below
        return Task.FromResult(added);
    });
builder.RegisterInstance(repo).As<IRepository<IAgentCall>>();
```

### NSubstitute 6 nullability (no `!`)

NSubstitute 6 annotates its API as nullable-aware, so under warnings-as-errors two idioms
now need a genuine null check (the repo-wide ban on `!`-suppression applies here too):

- **`CallInfo.Arg<T>()` returns `T?`.** When a `.Returns`/`.Do`/`.Throws` callback feeds the
  captured argument somewhere non-null (echoing it back inside `Task<T>`, adding it to a
  list, `TrySetResult`), assert it first with `ArgumentNullException.ThrowIfNull(arg)` and
  use the narrowed local — as in the `AddAsync` echo above. (A bare `string?` echo through
  `.Returns` is fine; only the nested-generic / non-`Returns` sinks need the guard.)
- **`Arg.Is<T>(x => …)` gives a nullable `x`.** The predicate is an *expression tree*, so
  `x is not null` is illegal (CS8122) — guard with `x != null && …` instead:
  `Arg.Is<AnomalyFlaggedEvent>(e => e != null && e.Blocked)`.

### Local registration helpers (allowed)

Private **static** helper methods that take a `ContainerBuilder` (or build a value object)
are fine — they are stateless and parameterized, not shared mutable fixtures. See
`ModelClientTests` (`RegisterEndpoint`, `MakeEndpoint`). The rule is *no shared state*, not
*no helper methods*.

### Seeding the database after the container is built

When a test needs rows present before the SUT runs, use `RegisterBuildCallback` or just
resolve repositories and add entities at the top of the test:

```csharp
IServiceProvider services = GetServices();
var models = services.GetRequiredService<IRepository<IModel>>();
await models.AddAsync(model, CancellationToken);
```

---

## Sharing an expensive fixture (escape hatch)

If — and only if — building per-test data is genuinely too expensive, `BuildContainer` is a
**static** helper that builds a container *without* registering it for per-test cleanup, so
a `[ClassInitialize]` can build one shared seeded container that the caller disposes in
`[ClassCleanup]`. This is a deliberate, last-resort escape hatch; the default remains a
fresh container per test. Do not reach for it to avoid writing a small amount of setup.

---

---

## Container-backed tests (rare)

Substituting the client library asserts how we *call* a driver, never how the server *replies* —
so it cannot catch a wire-format change (see #523: a RESP2→RESP3 switch passed the mocked Redis
suite unchanged). Where reply parsing or server-side semantics *are* the thing under test, start a
real service in a throwaway container instead:
`Proxytrace.Messaging.Tests/RedisIngestionStreamIntegrationTests.cs` is the reference.

Rules: build the container **inside the test method** (no shared fixture — the isolation rule still
applies), pin the image to the tag `docker-compose.yml` runs, and `Assert.Inconclusive` when no
runtime is reachable *unless* `PROXYTRACE_REQUIRE_DOCKER_TESTS` is set — `dotnet test` must not
require Docker locally, while CI must not skip silently. Full rationale in
[`docs/testing.md`](../../../docs/testing.md#container-backed-tests). Default to a mock; this is a
supplement for the cases a mock structurally cannot cover.

---

## Domain tests (`Proxytrace.Domain.Tests`)

Extend `DomainTest<Module>` for the convenient `GetOrCreate<T>` helper.

### Getting entities

```csharp
// Persisted entity (via generator) — use when you need a valid FK target
var suite = await GetOrCreate<ITestSuite>(services);

// Explicitly via the generator
var generator = services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>();
var suite = await generator.CreateAsync(CancellationToken);       // always new + persisted
var suite = await generator.GetOrCreateAsync(CancellationToken);  // reuse if already created
var inMemory = await generator.GenerateAsync(CancellationToken);  // in-memory only, not persisted
```

### Factory delegates

Resolve factory delegates from DI — **never `new` a domain entity**:

```csharp
var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
var group = factory(suite);

var createExisting = services.GetRequiredService<ITestRunGroup.CreateExisting>();
var group = createExisting(suite, status, completedAt, existingData);
```

### What to test on a new domain entity

1. **`CreateNew` happy path** — all properties set, `Id != Guid.Empty`, timestamps set.
2. **`CreateNew` null/invalid inputs** — one test per required property; assert it throws.
3. **`CreateExisting` round-trip** — create via generator, reconstitute via `CreateExisting`,
   assert all properties match including `Id`, `CreatedAt`, `UpdatedAt`.
4. **Unique IDs** — call `CreateNew` twice with identical inputs; IDs must differ.
5. **State-machine transitions** — one test per valid transition; one per invalid/terminal
   transition asserting it throws.
6. **Persistence** — after a state mutation, reload from the repository and assert the new
   state was saved.

### Asserting exceptions

```csharp
// Sync
var action = () => factory(null!);
action.Should().Throw<Exception>();

// Async
await FluentActions
    .Invoking(() => entity.SetRunning(CancellationToken))
    .Should().ThrowAsync<Exception>();
```

### Example — state transition test

```csharp
[TestMethod]
public async Task SetCompleted_FromRunning_TransitionsToCompletedWithTimestamp()
{
    IServiceProvider services = GetServices();
    var group = await CreateGroupInState(services, TestRunStatus.Running);

    var updated = await group.SetCompleted(CancellationToken);

    updated.Status.Should().Be(TestRunStatus.Completed);
    updated.CompletedAt.Should().NotBeNull();
    updated.CompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
}
```

---

## Application tests (`Proxytrace.Application.Tests`)

Use `BaseTest<Module>` and substitute infrastructure with NSubstitute (per *DI + NSubstitute*
above). Build everything inside the test method.

### Foreground vs background execution

Prefer `RunInForegroundAsync` in tests — it runs synchronously and returns the completed
entity, so there is no race condition to manage:

```csharp
var runner = services.GetRequiredService<ITestRunnerService>();
var testRun = await runner.RunInForegroundAsync(suite, endpoint, CancellationToken);
```

### Asserting persistence

Always reload from the repository to confirm the service actually persisted the change — do
not trust the returned in-memory object:

```csharp
var repo = services.GetRequiredService<IRepository<ITestRun>>();
var stored = await repo.GetAsync(testRun.Id, CancellationToken);
stored.TestResults.Should().HaveCount(1);
```

---

## Naming convention

```
[Subject]_[Condition]_[ExpectedOutcome]
```

Examples:
- `CreateNew_WithValidSuite_CreatesGroup`
- `SetCompleted_WhenAlreadyCompleted_Throws`
- `RunAsync_WhenResponseMatchesExpected_ProducesPassResult`

---

## Checklist before submitting

- [ ] No instance/static fields holding SUT, fakes, fixtures, or entities — everything is
      built inside the test method from `GetServices()`
- [ ] No `[TestFixture]`-style helper classes or shared setup objects; substitution is done
      through DI + NSubstitute
- [ ] Fakes you assert on are registered via `RegisterInstance` (same instance), not via the
      `InstancePerDependency` `RegisterStub`
- [ ] Class-wide overrides via `ConfigureContainer`; per-test overrides via `GetServices(action)`
- [ ] Each test has exactly one logical assertion focus
- [ ] `CancellationToken` passed to every async call
- [ ] State-machine tests cover both valid transitions and invalid/terminal-state attempts
- [ ] Persistence tests reload from the repository rather than trusting the returned object
- [ ] No `new` on domain entities — always use factory delegates from DI
- [ ] Verified with a **scoped** run — `dotnet test <the affected project>`, narrowed with
      `--filter "FullyQualifiedName~<Class>"` while iterating. Not the whole solution: CI runs
      `dotnet test Proxytrace.sln` on every push. See
      [`docs/testing.md`](../../../docs/testing.md#which-tests-to-run).
