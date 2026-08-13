# Publishing Nordstein.Core

Nothing is published yet. This file records how to publish and, more importantly, what must be
settled first — each of these is hard or impossible to walk back once packages are in
consumers' caches.

## Decide before the first publish

### 1. Where the packages live

Source stays private either way; the question is only who may restore the binaries.

| Option | Restore auth | Consequence for Proxytrace |
|--------|--------------|----------------------------|
| **nuget.org**, public packages | none | Proxytrace stays publicly buildable |
| **GitHub Packages**, private | PAT required, no anonymous read | Only authenticated builds work — anyone cloning Proxytrace hits a 401 on restore |
| **Azure Artifacts**, private | PAT / credential provider | Same as above, nicer feed tooling |

Proxytrace is currently public and Elastic-licensed, so a private feed would make it
unbuildable outside the organisation. Private source with public packages is the combination
that keeps both properties. If Proxytrace is going closed anyway, that constraint disappears
and GitHub Packages is the least setup.

### 2. The licence

`Directory.Build.props` packs `core/LICENSE`, currently a copy of Proxytrace's Elastic License
2.0, as a **placeholder**. A shared library distributed separately and consumed by products
that may not all be Elastic-licensed probably wants different terms. Settle this first: a
licence cannot be recalled from consumers who already restored the package.

### 3. The package ID prefix

Reserve the `Nordstein.*` prefix on nuget.org before the first public push, otherwise someone
else can take the next ID in the family.

## Publishing

Versioning is SemVer, one shared version across all Core packages, supplied by the pipeline:

```bash
dotnet pack core/Nordstein.Core.sln -c Release -p:NordsteinCoreVersion=1.2.3 -o core/artifacts
dotnet nuget push "core/artifacts/*.nupkg" --source <feed> --api-key <key> --skip-duplicate
```

The `.snupkg` symbol packages are pushed the same way and are worth pushing: without symbols and
SourceLink, stepping into Core from a consuming product stops working, which is the change
people notice and resent most about a package split.

Publish CI-build prereleases (`1.3.0-ci.<run>`) from every merge to Core's default branch, so a
product can validate an unreleased Core without a tag. The `core-package` CI job already builds
exactly these; it just does not push them.

## Consuming a published version

Set the version once, in the repository root `Directory.Build.props`:

```xml
<NordsteinCoreVersion Condition="'$(NordsteinCoreVersion)' == ''">1.2.3</NordsteinCoreVersion>
```

Pin exact versions and let Dependabot raise the bumps. Floating ranges across several packages
turn one bad publish into a failure in every product at once, with no diff to point at.

## Completing the split

```bash
git subtree split --prefix=core -b core-extraction
# push that branch as the initial history of the Core repository
```

Then, in this repository: delete `core/`, and point `NordsteinCorePath` at a sibling checkout
(`../Core/`) so source mode keeps working for anyone with both repositories cloned. Everything
else — the `NordsteinCoreReference` items, the Dockerfile restore layers, CI — is already
written against the switch and does not change.
