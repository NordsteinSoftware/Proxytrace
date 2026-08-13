# Nordstein.Core

Product-agnostic foundation code shared across Nordstein applications. Nothing in here knows
what Proxytrace is; nothing in here may ever learn.

| Package | Contents |
|---------|----------|
| [`Nordstein.Core.Common`](Nordstein.Core.Common) | Clock/randomness seams, async primitives, validation, type conversion, Autofac helpers, hosting defaults |
| [`Nordstein.Core.Testing`](Nordstein.Core.Testing) | `BaseTest<TModule>` and the shared MSTest + AwesomeAssertions + NSubstitute harness |

## Why it lives here for now

Core is destined for its own private repository, published as NuGet packages. It sits under
`core/` in the Proxytrace repository as a **staging area** so the extraction can be validated —
packaging, namespaces, the dependency direction, CI — before anything is split or published.

Completing the move keeps the history, but **not** via `git subtree split`: that filters by path
without following renames, and everything here was `git mv`'d from `Proxytrace.Common/` and
friends, so it would yield a single commit. `git filter-repo` with the old paths mapped onto the
new ones carries the real history across. The verified recipe is in
[`PUBLISHING.md`](PUBLISHING.md#completing-the-split).

The staging period is not free: nothing but review discipline stops a Proxytrace type from
being referenced in here, and the day that happens the extraction stops being possible. The two
guards against it are the standalone `Nordstein.Core.sln`, which CI builds and tests on its own,
and the `core-package` CI job, which rebuilds the product against the packed `.nupkg` files
instead of the sources.

## The one rule

**Core may not reference the product. Ever.** The dependency arrow points one way: Proxytrace
depends on Core. A Core type that needs to know about an agent, a trace, or a project belongs in
Proxytrace, not here — and if it feels like it belongs in both, it needs a seam (an interface
Core declares and the product implements), not a reference.

## Consuming it

A consuming project declares an item, not a reference:

```xml
<ItemGroup>
  <NordsteinCoreReference Include="Nordstein.Core.Common" />
</ItemGroup>
```

`Directory.Build.targets` in the repository root expands it into either a `ProjectReference`
(source mode) or a `PackageReference` (package mode). See
[`docs/code-reuse.md`](../docs/code-reuse.md) for the full picture and
[`PUBLISHING.md`](PUBLISHING.md) for what still has to be decided before the first publish.

## Building

```bash
dotnet build core/Nordstein.Core.sln
dotnet test  core/Nordstein.Core.sln
```

Core builds and tests without Proxytrace — that is the property being protected. If it ever
stops being true, the extraction has regressed.
