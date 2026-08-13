# Code Reuse — Nordstein.Core

Proxytrace is the first Nordstein product, not the only one. The parts of it that are not about
LLM tracing — the clock and randomness seams, validation helpers, Autofac wiring, hosting
defaults, the test harness — are the parts a second product would otherwise reimplement badly.
Those parts live in [`core/`](../core/) as **Nordstein.Core**, on their way to a separate private
repository and NuGet packages.

This page is about the mechanism. [`core/PUBLISHING.md`](../core/PUBLISHING.md) covers the
decisions still open before anything is published.

## What is in Core today

| Package | Contents |
|---------|----------|
| `Nordstein.Core.Common` | `IClock`, `IRandom`, `IAsyncLock`, validation helpers, `ITypeConverter`, Autofac registration helpers, `AddResilientBackgroundServices`, `IAppVersion`, `Sha256`, slug/log-safe text helpers |
| `Nordstein.Core.Testing` | `BaseTest<TModule>` and the MSTest + AwesomeAssertions + NSubstitute baseline |

This is deliberately the least entangled slice. The bigger prizes — the domain-entity and
repository foundation (`AbstractRepository`, `AbstractEntityConfiguration`, `IRepository`,
`IDomainEntity`), and whole subsystems like licensing, audit logging, the secret-protection seam
and user/auth/MFA — are **not** extracted yet. See [What comes next](#what-comes-next).

## The one rule

**Core may not reference the product.** Proxytrace depends on Core; nothing points back. A Core
type that would need to know about an agent, a trace, or a project belongs in
`Proxytrace.Domain` instead. When something genuinely belongs on both sides, the split is an
interface Core declares and the product implements — not a reference.

Two things enforce it rather than trusting review:

- `core/Nordstein.Core.sln` builds and tests standalone, and CI runs it that way.
- The `core-package` CI job packs Core and rebuilds the whole product against the `.nupkg`
  files. That is what catches a type that is public in source but missing from the package
  surface, and a dependency Core forgot to declare because source mode resolved it through
  Proxytrace's own graph.

## How a project consumes Core

Declare an item, not a reference:

```xml
<ItemGroup>
  <NordsteinCoreReference Include="Nordstein.Core.Common" />
</ItemGroup>
```

[`Directory.Build.targets`](../Directory.Build.targets) expands it into one of two things:

| Mode | Expansion | When |
|------|-----------|------|
| **source** | `ProjectReference` into `core/` | `core/Nordstein.Core.sln` exists (the default today) |
| **package** | `PackageReference` at `$(NordsteinCoreVersion)` | `-p:UseLocalCore=false`, or Core's sources are absent |

The indirection exists so a Core change stays a one-build edit. Without it, every cross-boundary
change becomes edit → pack → bump → restore → retest, and the predictable result is that nobody
makes small Core improvements any more — they copy the code into the product instead, which is
the exact failure the extraction is meant to prevent.

Both modes are exercised on every backend CI run, so neither can rot.

### Overrides

```bash
# force package mode against a locally packed feed
dotnet build Proxytrace.sln -p:UseLocalCore=false -p:NordsteinCoreVersion=0.1.0-dev \
    -p:RestoreAdditionalProjectSources=core/artifacts

# point at a sibling Core checkout (what this becomes once Core is its own repository)
dotnet build Proxytrace.sln -p:NordsteinCorePath=../Core/
```

The version lives in one place, [`Directory.Build.props`](../Directory.Build.props), so a Core
bump is a one-line diff.

## Why `core/` is still inside this repository

Core is staged here rather than extracted in one move, so the mechanism could be validated —
packaging, namespaces, dependency direction, Docker restore layers, CI — before a second
repository exists to get any of it wrong. Its history is confined to the `core/` prefix, so
completing the move is a `git subtree split` on that prefix, not a copy that drops the past.

The cost of staging is that only the two guards above keep the boundary honest. Do not weaken
them.

## What comes next

Roughly in order of value per unit of pain:

1. **The domain/storage foundation** — `IDomainEntity`, `IRepository`, `AbstractRepository`,
   `AbstractEntityConfiguration`, `Entity`, `EntityCache`, `AmbientDbContext`,
   `StorageConfiguration`. Three things block it: `Domain.Module`/`Storage.Module` discover
   types by reflecting over `typeof(Module).Assembly`, so the scan must take the consuming
   product's assemblies as a parameter; almost all of the Storage foundation is `internal` and
   reachable only through `InternalsVisibleTo`, so the seams must become public API; and
   `MigrationsAssembly` currently pins migrations to `Proxytrace.Storage`, so Core must ship
   none and each product must own its own migrations assembly.
2. **Licensing** — `Proxytrace.Licensing` is already product-agnostic (tiers, features, limits,
   JWT verification, offline grace). Mostly a move.
3. **Cross-cutting subsystems** — the secret-protection seam, the audit-log pipeline,
   user/auth/MFA/invites/API keys, the application-error log, notifications, the SSE broadcaster
   infrastructure.
4. **Frontend** — the `frontend/src/components/ui/` primitives, the Lingui setup and the SSE
   hooks, as an `@nordstein/ui` npm package. Same question, different registry; do it after the
   backend split rather than alongside it.

Never extracted: anything that knows what an agent, a trace, an evaluator or a test run is.
