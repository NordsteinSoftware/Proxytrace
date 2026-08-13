# Creating a New Domain Entity in Proxytrace

Use this guide when implementing a new domain concept end-to-end. Follow the steps in order.
`ITestRunGroup` is the canonical reference implementation — read it when a pattern is unclear.

---

## Checklist

- [ ] Domain interface (`Proxytrace.Domain/[Entity]/I[Entity].cs`)
- [ ] Domain implementation (`Proxytrace.Domain/[Entity]/Internal/[Entity].cs`)
- [ ] Generator (`Proxytrace.Domain/[Entity]/Internal/[Entity]Generator.cs`)
- [ ] Repository interface if needed (`Proxytrace.Domain/[Entity]/I[Entity]Repository.cs`)
- [ ] Storage entity (`Proxytrace.Storage/Internal/Entities/[Entity]/[Entity]Entity.cs`)
- [ ] Storage config + mapper (`Proxytrace.Storage/Internal/Entities/[Entity]/[Entity]Config.cs`)
- [ ] Storage repository if needed (`Proxytrace.Storage/Internal/Entities/[Entity]/[Entity]Repository.cs`)
- [ ] EF Core migration
- [ ] Tests (`Proxytrace.Domain.Tests/[Entity]ValidationTests.cs`)

---

## Step 1 — Domain interface

File: `Proxytrace.Domain/[Entity]/I[Entity].cs`

Rules:
- `public interface` extending `IDomainEntity` (gives `Id`, `CreatedAt`, `UpdatedAt` for free — do not redeclare them)
- Two and only two factory delegates: `CreateNew` and `CreateExisting`
- `CreateExisting` has the same positional parameters as `CreateNew` plus a trailing `IDomainEntityData existing`
- 1:N parent references are the full domain interface (e.g. `ITestSuite Suite`), never a raw `Guid`
- No `IData` sub-interface — all properties live directly on the interface

```csharp
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunGroup;

public interface ITestRunGroup : IDomainEntity
{
    public delegate ITestRunGroup CreateNew(ITestSuite suite);
    public delegate ITestRunGroup CreateExisting(
        ITestSuite suite,
        TestRunStatus status,
        DateTimeOffset? completedAt,
        IDomainEntityData existing);
    
    ITestSuite Suite { get; }
    TestRunStatus Status { get; }
    DateTimeOffset? CompletedAt { get; }

    Task<ITestRunGroup> SetRunning(CancellationToken cancellationToken = default);
    Task<ITestRunGroup> SetCompleted(CancellationToken cancellationToken = default);
    Task<ITestRunGroup> SetFailed(CancellationToken cancellationToken = default);
    Task<ITestRunGroup> SetCancelled(CancellationToken cancellationToken = default);
}
```

---

## Step 2 — Domain implementation

File: `Proxytrace.Domain/[Entity]/Internal/[Entity].cs`

Rules:
- `internal sealed record` extending `DomainEntity<I[Entity]>` and implementing `I[Entity]`
- Two constructors that mirror the factory delegates exactly:
  - "new" constructor calls `base(repository)` — DI injects `IRepository<I[Entity]>`
  - "existing" constructor calls `base(existing, repository)`
- No primary constructors — use classic constructor injection
- All properties are get-only (set in constructor body) 
- We use immutability by convention!
- `Validate` yields `base.Validate(...)` first, then cascades into referenced entities
- Mutation methods return a new record instance via `repository.UpdateAsync(...)` — never mutate in place

```csharp
internal record TestRunGroup : DomainEntity<ITestRunGroup>, ITestRunGroup
{
    public ITestSuite Suite { get; }
    public TestRunStatus Status { get; }
    public DateTimeOffset? CompletedAt { get; }

    public TestRunGroup(
        ITestSuite suite, 
        IRepository<ITestRunGroup> repository) : base(repository)
    {
        Suite = suite;
        Status = TestRunStatus.Pending;
        CompletedAt = null;
    }

    public TestRunGroup(
        ITestSuite suite,
        TestRunStatus status,
        DateTimeOffset? completedAt,
        IDomainEntityData existing,
        IRepository<ITestRunGroup> repository) : base(existing, repository)
    {
        Suite = suite;
        Status = status;
        CompletedAt = completedAt;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;
        // cascade into owned references:
        foreach (var result in Suite.Validate(validationContext))
            yield return result;
        if(CompletedAt.HasValue) 
        {
            yield return Validation.InPast(CompletedAt.Value);
        }
    }

    public Task<ITestRunGroup> SetRunning(CancellationToken ct = default)
        => SetState(TestRunStatus.Running, ct);

    // ... other transition methods ...

    private Task<ITestRunGroup> SetState(TestRunStatus state, CancellationToken ct)
    {
        if (IsTerminal(Status))
            throw new InvalidOperationException($"Cannot change group {Id} from {Status} to {state}.");

        DateTimeOffset? completedAt = IsTerminal(state) ? DateTimeOffset.UtcNow : null;
        var updated = new TestRunGroup(Suite, state, completedAt, this, repository);
        return repository.UpdateAsync(updated, ct);
    }

    private static bool IsTerminal(TestRunStatus s)
        => s is TestRunStatus.Completed or TestRunStatus.Failed or TestRunStatus.Cancelled;
}
```

### Validation helpers (from `Nordstein.Core.Common.Validation`)

```csharp
yield return Validation.NotNullOrWhiteSpace(Name);  // note capital S
yield return Validation.NotNull(Suite);
yield return Validation.NotDefault(SomeGuid);
yield return Validation.InPast(CreatedAt);
yield return Validation.NotBefore(UpdatedAt, CreatedAt);
...
```

---

## Step 3 — Generator

File: `Proxytrace.Domain/[Entity]/Internal/[Entity]Generator.cs`

The generator is used exclusively by the test infrastructure. 
It must be able to produce a valid, self-contained instance.
GenerateAsync calls produce instances, but do not persist them.
CreateAsync must persist it.

```csharp
internal class TestRunGroupGenerator : DomainEntityGenerator<ITestRunGroup>
{
    private readonly ITestRunGroup.CreateNew factory;
    private readonly IDomainEntityGenerator<ITestSuite> suiteGenerator;

    public TestRunGroupGenerator(
        ITestRunGroup.CreateNew factory,
        IRepository<ITestRunGroup> repository,
        IDomainEntityGenerator<ITestSuite> suiteGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.suiteGenerator = suiteGenerator;
    }

    public override async Task<ITestRunGroup> GenerateAsync(CancellationToken ct = default)
    {
        var suite = await suiteGenerator.GetOrCreateAsync(ct);
        return factory(suite);
    }
}
```

Rules:
- Constructor takes `IRepository<I[Entity]>` and `IRandom` (required by base) plus any generators for referenced entities
- `GenerateAsync` calls `GetOrCreateAsync` on referenced generators to avoid creating redundant rows
- Call `CreateAsync` instead when the test genuinely needs a fresh independent instance

---

## Step 4 — Repository interface (only if needed)

File: `Proxytrace.Domain/[Entity]/I[Entity]Repository.cs`

Only create this if you need queries beyond the standard CRUD that `IRepository<T>` already provides — e.g. filtering by a parent entity:

```csharp
public interface ITestRunGroupRepository : IRepository<ITestRunGroup>
{
    Task<IReadOnlyList<ITestRunGroup>> GetByAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);
}
```

If you only need `GetAsync`, `AddAsync`, `UpdateAsync`, etc., skip this file and use `IRepository<I[Entity]>` directly.

---

## Step 5 — Storage entity

File: `Proxytrace.Storage/Internal/Entities/[Entity]/[Entity]Entity.cs`

Rules:
- `internal record` extending `Entity`
- Decorated with `[StoredDomainEntity(typeof(I[Entity]))]` — this is what reflection-based DI uses
- **FK columns are `Guid`, not the domain interface** — the inverse of the domain layer
- `required` properties with `init` accessors
- No navigation properties unless needed for EF Core N:M join tables

```csharp
[StoredDomainEntity(typeof(ITestRunGroup))]
internal record TestRunGroupEntity : Entity
{
    public required Guid Suite { get; init; }
    public required TestRunStatus Status { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
```

---

## Step 6 — Storage config and mapper

File: `Proxytrace.Storage/Internal/Entities/[Entity]/[Entity]Config.cs`

Rules:
- Extends `AbstractEntityConfiguration<[Entity]Entity>`
- Also implements `IMapper<I[Entity], [Entity]Entity>` — one class does both jobs
- Constructor takes repositories for any referenced entities plus the `CreateExisting` factory and `ISerializer` if JSON columns are needed
- `Configure` sets up FK relationships — use `DeleteBehavior.Cascade` for owned children, `DeleteBehavior.Restrict` for optional references
- `Map(stored → domain)` loads referenced entities from their repositories (parallel with `Task.WhenAll` when there are multiple)
- `Map(domain → stored)` is a pure synchronous projection wrapped in `.ToTaskResult()`

```csharp
internal class TestRunGroupConfig
    : AbstractEntityConfiguration<TestRunGroupEntity>,
      IMapper<ITestRunGroup, TestRunGroupEntity>
{
    private readonly IRepository<ITestSuite> suites;
    private readonly ITestRunGroup.CreateExisting factory;

    public TestRunGroupConfig(IRepository<ITestSuite> suites, ITestRunGroup.CreateExisting factory)
    {
        this.suites = suites;
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<TestRunGroupEntity> builder)
    {
        builder
            .HasOne<TestSuiteEntity>()
            .WithMany()
            .HasForeignKey(e => e.Suite)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public async Task<ITestRunGroup> Map(TestRunGroupEntity stored, CancellationToken ct = default)
        => factory(
            suite: await suites.GetAsync(stored.Suite, ct),
            status: stored.Status,
            completedAt: stored.CompletedAt,
            existing: stored);

    public Task<TestRunGroupEntity> Map(ITestRunGroup domain, CancellationToken ct = default)
        => new TestRunGroupEntity
        {
            Id = domain.Id,
            Suite = domain.Suite.Id,
            Status = domain.Status,
            CompletedAt = domain.CompletedAt,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
```

### FK delete behaviour

| Relationship | `OnDelete` |
|---|---|
| Owned child (entity can't exist without parent) | `Cascade` |
| Optional / shared reference | `Restrict` |

### Loading multiple FK references — sequentially, never in parallel

```csharp
public async Task<IMyEntity> Map(MyEntity stored, CancellationToken ct = default)
{
    IParent parent = await parents.GetAsync(stored.Parent, ct);
    IOther other = await others.GetAsync(stored.Other, ct);
    return factory(parent, other, stored);
}
```

**Do NOT `Task.WhenAll` the loads.** `Func<StorageDbContext>` returns `ambient.Context` whenever a
transaction is open, so *inside* a transaction every repository shares **one** `StorageDbContext` —
and two concurrent operations on one context throw `InvalidOperationException: A second operation
was started on this context instance`. The entity cache does not save you either: `CanUseCache` is
`cache is not null && !ambient.IsActive`, so a transaction suppresses the cache and both loads
become real database round trips.

A mapper cannot know whether its caller opened a transaction, and controllers routinely wrap writes
in `transaction.InvokeAsync`, so a parallel mapper is a latent 500 on every transactional path.
Every shipped config in `Proxytrace.Storage` loads sequentially — follow them. The loads are
indexed point lookups, so there is nothing to win.

> This bit for real: `CostLimitConfig` shipped a `Task.WhenAll` mapper and 500'd on creating an
> agent-scoped cost limit — the only branch that loads two entities. **The in-memory provider does
> not reproduce it** (it does not enforce the single-operation guard), so the unit suites passed and
> only the Postgres-backed e2e run caught it.

---

## Step 7 — Storage repository (only if needed)

File: `Proxytrace.Storage/Internal/Entities/[Entity]/[Entity]Repository.cs`

Rules:
- `internal class` extending `AbstractRepository<I[Entity], [Entity]Entity>`
- Implements the custom repository interface from Step 4
- **Must be decorated with `[UsedImplicitly]`** — reflection-based DI won't find it otherwise
- Use `AsNoTracking()` on all read queries

```csharp
[UsedImplicitly]
internal class TestRunGroupRepository
    : AbstractRepository<ITestRunGroup, TestRunGroupEntity>,
      ITestRunGroupRepository
{
    public TestRunGroupRepository(
        IMapper<ITestRunGroup, TestRunGroupEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction) : base(mapper, contextFactory, transaction) { }

    public async Task<IReadOnlyList<ITestRunGroup>> GetByAgentAsync(
        Guid agentId,
        CancellationToken ct = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                g => g.Suite,
                s => s.Id,
                (g, s) => new { Group = g, Suite = s })
            .Where(x => x.Suite.Agent == agentId)
            .Select(x => x.Group)
            .ToListAsync(ct);

        return await Map(stored, ct);
    }
}
```

---

## Step 8 — EF Core migration

Migrations are PostgreSQL-only — supply the design-time connection string and `--startup-project`:

```bash
ConnectionStrings__Default="Host=localhost;Port=5432;Database=proxytrace;Username=proxytrace;Password=proxytrace" \
  dotnet ef migrations add Add[Entity] --project Proxytrace.Storage --startup-project Proxytrace.Api
```

EF generates the structural migration automatically. If the migration needs to backfill existing rows (e.g. creating a parent record for each existing child), add raw SQL after the `CreateTable` call:

```csharp
// After CreateTable(...) — PostgreSQL identifiers are double-quoted
migrationBuilder.Sql(@"
    INSERT INTO ""[Entity]Entity"" (""Id"", ""Suite"", ""Status"", ""CreatedAt"", ""UpdatedAt"")
    SELECT ""Id"", ""FkColumn"", 0, ""CreatedAt"", ""UpdatedAt""
    FROM ""OtherEntity"";
");

migrationBuilder.Sql(@"
    UPDATE ""OtherEntity"" SET ""FkColumn"" = ""Id"";
");
```

Always verify the generated migration file before applying it — check column types, nullable settings, and that FKs point to the right tables.

---

## Step 9 — Tests

File: `Proxytrace.Domain.Tests/[Entity]ValidationTests.cs`

See `/test` for the full test-writing guide. For a new domain entity, cover: `CreateNew` happy path, null/invalid inputs, unique IDs, `CreateExisting` round-trip, state-machine transitions (valid and terminal-state violations), and persistence reloads. `TestRunGroupValidationTests` is the canonical reference.

---

## Common mistakes

| Mistake | Correct approach |
|---|---|
| `new MyEntity(...)` in production code | Resolve `I[Entity].CreateNew` from DI |
| Putting `Suite` on both parent and child | Suite lives at exactly one level |
| Forgetting `[UsedImplicitly]` on the repository | Reflection-based DI silently skips it |
| `DeleteBehavior.Cascade` on a shared reference | Use `Restrict`; `Cascade` only for owned children |
| Mutating state in-place | Return `repository.UpdateAsync(new Record(...), ct)` |
| Forgetting `[StoredDomainEntity(...)]` | Entity won't be discovered by the storage module |
| Loading FK targets sequentially | Use `Task.WhenAll` in the mapper |
