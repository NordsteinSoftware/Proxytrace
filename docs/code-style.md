# Backend Code Style

> **`TreatWarningsAsErrors=true`** is set solution-wide in `Directory.Build.props` — the build
> fails on *any* compiler warning. Leave no unused usings/variables, no obsolete-API calls, no
> nullable warnings. This is why suppressing nullable warnings with `!` is both forbidden and
> pointless (it would only move the failure). Run `dotnet build Proxytrace.sln` before claiming done.

- Dependency Injection is super important - use it whenever possible. Avoid the static keyword and service locators.
- Do not use primary constructors. Use constructor injection with DI and `this(...)` chaining for domain entities.
- Use `record` types for all domain entities and storage entities (even if mutable)
- Make types `internal` by default; only interfaces or POCO types should be `public`
- Use `required` properties with `init` accessors for storage entities
- Prefer immutability and statelessness; storage entities can be mutable if needed for EF Core
- Use `var` when the type is obvious from the right-hand side, otherwise be explicit
- Use expression-bodied members for simple one-liners; otherwise use block bodies with braces
- Use `this(...)` constructor chaining to avoid duplication between "new" and "existing" constructors on domain entities
- Use `nameof(...)` for all parameter names in exceptions and validation
- Prefer collection expressions when possible
- Supressing nullable warnings with `!` is strictly forbidden everywhere!
- Injecting `IServiceProvider` shall be strongly avoided
- Static members shall be avoided (except for extension methods and constants)
- Docstrings: newline after `<summary>` and before `</summary>` (minimum 3-line blocks), e.g.:
  ```csharp
  /// <summary>
  /// Does the thing.
  /// </summary>
  ```

## Key Conventions

- All timestamps are `DateTimeOffset`, never `DateTime`
- Domain entities are immutable `internal record` types — no setters on domain-layer properties
- Domain interfaces are `public`; implementations and storage entities are `internal`
- Repositories return domain entities (`I[Entity]`), never storage entities
- Always pass `CancellationToken` to every async method
- Domain references hold the related entity (e.g. `IModelEndpoint`, `IReadOnlyCollection<IEvaluator>`); storage entities hold the `Guid` foreign key
- Storage entities use `required` properties with `init` accessors and extend `Entity`
- Decorate custom storage repositories with `[UsedImplicitly]` so reflection-based DI discovers them

## Concurrency

- **Always use `IAsyncLock` for in-process concurrency control.** Inject it via DI
  (`IAsyncLock` from `Nordstein.Core.Common.Async`, registered in `Nordstein.Core.Common.Module`).
  Never use `lock`/`Monitor`, `SemaphoreSlim`, `Mutex`, or other raw synchronization primitives
  directly in feature code — they are not safe to hold across `await`, and a hand-rolled lock
  bypasses the shared, keyed implementation.
- **Narrow exception:** a purely-synchronous critical section that contains **no `await`** and
  guards low-level non-DI infrastructure may use `System.Threading.Lock` when an async, keyed lock
  buys no safety. This is rare and must be justified with a comment pointing here. Current sanctioned
  uses: the Lucene index writer/indexing service (`Proxytrace.Application/Search/Internal`), which
  serialize synchronous Lucene operations. Everything else uses `IAsyncLock`.
- **Second narrow exception — counting, not excluding.** `IAsyncLock` grants *one* holder per key, so
  it cannot express "at most N concurrently". Where the requirement is a **concurrency limit** rather
  than mutual exclusion, `SemaphoreSlim` is the correct primitive and is permitted, with a comment
  pointing here. Current sanctioned use: `TestRunnerService.modelCallGate`, which caps in-flight
  upstream model calls at `TestRunnerConfiguration.MaxDegreeOfParallelism` across the three nested
  `Parallel.ForEachAsync` loops (which otherwise multiply that setting rather than respecting it).
  A limiter must never be held while acquiring another lock — the gate is released before a test
  case's evaluators run, so the nesting cannot deadlock.
- `IAsyncLock` is **keyed**: `LockAsync(key, ct)` serializes only callers sharing the same `key`,
  so use the narrowest natural key (e.g. an entity `Id`, a fingerprint) to avoid serializing
  unrelated work. Pass the `CancellationToken` through.
- Always `await` the acquire and scope the handle with `using` so it releases on every path:
  ```csharp
  private readonly IAsyncLock asyncLock; // injected via constructor

  using IDisposable sync = await asyncLock.LockAsync(entity.Id, cancellationToken);
  // critical section — safe across awaits
  ```
- Prefer the async `LockAsync`; only use the synchronous `Lock(key)` when no `await` is possible
  in the critical section.
- Reference usages: `TestRunnerService`, `AgentRepository`, `LiteLlmCatalogResolver`.
