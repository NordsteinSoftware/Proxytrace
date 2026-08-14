# Code Reuse — Nordstein.Core

Proxytrace is the first Nordstein product, not the only one. The parts of it that are not about
LLM tracing — the clock and randomness seams, validation helpers, Autofac wiring, hosting
defaults, the test harness — are the parts a second product would otherwise reimplement badly.
Those parts live in **Nordstein.Core** — its own public repository at
[NordsteinSoftware/Nordstein.Core](https://github.com/NordsteinSoftware/Nordstein.Core), mounted
here as a git submodule at [`core/`](../core/). Publishing it as NuGet packages is a later,
separate step.

This page is about the mechanism — how Proxytrace references Core and what has moved. The Core
side has its own documentation: [`core/CLAUDE.md`](../core/CLAUDE.md) states the (stricter)
standard Core work is held to, and [`core/docs/`](../core/docs/README.md) documents the
foundation itself (architecture/DI, the domain and storage bases, the test harness, validation,
the licensing engine, plus a product playbook of transferable practices). **Work under `core/`
follows those pages, not just this one.** [`core/PUBLISHING.md`](../core/PUBLISHING.md) covers
the decisions still open before anything is published.

## What is in Core today

| Package | Contents |
|---------|----------|
| `Nordstein.Core.Common` | `IClock`, `IRandom`, `IAsyncLock`, validation helpers, `ITypeConverter`, `ISecretProtector`, `ISecretHasher`, Autofac registration helpers, `AddResilientBackgroundServices`, `IAppVersion`, `Sha256`, slug/log-safe text helpers |
| `Nordstein.Core.Domain` | `IDomainObject`, `IDomainEntity`, `IRepository`, `ITransaction`, archive contracts, entity/generator bases, paging, persistence exceptions, entity events, consuming-assembly Autofac discovery |
| `Nordstein.Core.Testing` | `BaseTest<TModule>` and the MSTest + AwesomeAssertions + NSubstitute baseline |
| `Nordstein.Core.Licensing` | The generic license engine: JWT verification (issuer/audience/keys injected), runtime activation, the resolved `LicenseSnapshot`, and the periodic server check with the offline-grace state machine. Tier/feature/limit vocabulary is supplied by the product through `ILicenseTierPolicy` (string names that double as the JWT claim values) |
| `Nordstein.Core.Storage` | `NordsteinDbContext` base, `Entity`/`IEntity`/`IArchivableEntity`, `AbstractRepository`/`ArchivableRepository`, the `AmbientDbContext`/`ITransaction` seam, `IMapper`, `EntityCache`/`EntityCacheVersions`, `AbstractEntityConfiguration`, `LikePattern`, the concurrency-token helpers, and `StorageFoundationModule<TContext>` (assembly-scoped entity/config/repository/cache discovery). Provider-neutral — depends only on EF Core Relational |
| `Nordstein.Core.AI` | The generic AI vocabulary: LLM message/conversation types, tool specifications, prompt templates (`IPromptTemplate`/`IPrompt`), completions/`TokenUsage`/`IModelParameters`, the versionless `IAgent` and `IModelClient` contracts with their option records, structured model-output parsing (`IOutputFormat`, `ITextSerializer`, truncated-JSON repair), and the Autofac `Module` wiring generators, JSON converters, serializer, and output formats. See [`core/docs/ai.md`](../core/docs/ai.md) |

The reusable domain contracts, **the storage foundation**, **the licensing engine**, and **the AI
foundation** (`Nordstein.Core.AI` — messages, tools, prompts, completions, the versionless agent
and model-client contracts, output parsing; the former `Proxytrace.Serialization` project is
retired into it) are now extracted. For AI, the product keeps everything that ties an agent to
Proxytrace: `Proxytrace.Domain.Agent.IAgent` extends the Core contract with version history,
endpoint binding, tenancy, search and archiving; `ModelClientFactory`/`ModelOptionsFactory` are
the product seams over the Core client contract; the OpenAI `ModelClient` implementation,
providers/endpoints/pricing, and the prompt-template repository stay product-side. What stays in the product is the provider- and schema-specific storage code: the concrete
`StorageDbContext` (a thin `NordsteinDbContext` subclass), the `StorageConfiguration` provider
switch (Npgsql / in-memory), the migrations assembly and its snapshot, the concrete
entities/configurations/repositories, the statistics/query stores, and the backfills. The foundation
is threaded off a `DbContext` base so it is product-context-agnostic, and off EF Core Relational
only so it is provider-agnostic — each product supplies its own provider, concrete context and
migrations. For licensing, `Proxytrace.Licensing` keeps the product policy — the
`LicenseFeature`/`LicenseLimit`/`LicenseTier` enums, `LicensePolicy`, the issuer/audience, the
`LicensePublicKeys` trust root, and thin enum-typed adapters over the engine's string vocabulary —
while the engine (validation, activation, snapshot, server check, offline grace) lives in
`Nordstein.Core.Licensing`; see [`docs/licensing.md`](licensing.md) and
[`core/docs/licensing.md`](../core/docs/licensing.md). Whole subsystems like audit logging,
product-specific secret protection, and user/auth/MFA are **not** extracted yet. See
[What comes next](#what-comes-next). The Core-side references are indexed in
[`core/docs/README.md`](../core/docs/README.md); for the storage foundation specifically, see
[`core/docs/storage.md`](../core/docs/storage.md) and [`core/docs/domain.md`](../core/docs/domain.md).

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
# force package mode against a locally packed feed (Directory.Build.props adds core/artifacts
# as a restore source by absolute path whenever it exists — do not pass a relative source, NuGet
# resolves it per project directory)
dotnet build Proxytrace.sln -p:UseLocalCore=false -p:NordsteinCoreVersion=0.1.0-dev

# point at a Core checkout elsewhere instead of the core/ submodule (e.g. a shared working copy)
dotnet build Proxytrace.sln -p:NordsteinCorePath=../Core/
```

The version lives in one place, [`Directory.Build.props`](../Directory.Build.props), so a Core
bump is a one-line diff.

## How `core/` is wired

`core/` is a **git submodule** of the Nordstein.Core repository, pinned to a specific commit.
Source mode compiles that checkout directly, so a Core change stays a one-build edit and the
Dockerfiles, CI and `detect-changes` see the same `core/` paths they always have. Making a Core
change means committing it in the Nordstein.Core repository, then bumping the submodule pointer
here.

Clone the product with its submodule, or `core/` is empty and the build silently drops to package
mode:

```bash
git clone --recurse-submodules <proxytrace-repo>
git submodule update --init        # if you already cloned without it
```

Because Core is genuinely a separate repository that builds and tests on its own (the `backend` CI
job runs `core/Nordstein.Core.sln` standalone) and the `core-package` job rebuilds the product
against its packages, [the one rule](#the-one-rule) is enforced mechanically, not by review.

Core's original Common and Testing packages were extracted from this repository with
`git filter-repo` — **not** `git subtree split`, which filters by path without following renames and
would have reduced that code's history to the single commit that moved it. Domain was added in a
later extraction tranche. The verified original recipe is in
[`core/PUBLISHING.md`](../core/PUBLISHING.md#how-this-repository-was-extracted).

## What comes next

Roughly in order of value per unit of pain (the storage foundation and the licensing engine —
items 1 of earlier tranches — are **done**: `Nordstein.Core.Storage` owns `NordsteinDbContext`, the
repositories, the ambient-transaction seam, the cache, the EF-config base and
`StorageFoundationModule<TContext>`; `Nordstein.Core.Licensing` owns the license engine
([#539](https://github.com/NordsteinSoftware/Proxytrace/issues/539)) while the product keeps its
tier policy, enums and trust root):

1. **Cross-cutting subsystems** — the audit-log pipeline, product-specific secret-protection
   implementations and blind indexes, user/auth/MFA/invites/API
   keys, the application-error log, notifications, and the SSE broadcaster infrastructure. The
   generic `ISecretProtector` and `ISecretHasher` contracts are already in Core; Proxytrace keeps the
   Data Protection purpose, key-ring path, hash implementation, and persisted blind-index scheme.
2. **Frontend** — the `frontend/src/components/ui/` primitives, the Lingui setup and the SSE
   hooks, as an `@nordstein/ui` npm package. Same question, different registry; do it after the
   backend split rather than alongside it.

Never extracted: anything that knows what a trace, a project, an evaluator or a test run is. The
*generic* agent vocabulary (messages, tools, prompts, completions, the versionless agent and
model-client contracts, output parsing) lives in `Nordstein.Core.AI`; what stays product-side is
everything that ties an agent to this product — tenancy, version history, endpoints and pricing,
the OpenAI `ModelClient` implementation, ingestion, and search.
