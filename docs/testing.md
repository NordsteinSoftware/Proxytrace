# Testing Conventions

**Before writing or modifying any backend test, you MUST invoke the `test` skill
(`.claude/skills/test/SKILL.md`) and follow it.** It is the source of truth for the test
harness: per-test `BaseTest<TModule>` containers, the `ConfigureContainer` / `GetServices`
DI hooks, NSubstitute substitution patterns, and the hard rules against shared
state/fields and `[TestFixture]`-style helper classes. The summary below is orientation
only; the skill overrides it where they differ.

## Which tests to run

The backend suite is ~2,200 tests across 10 projects; the frontend has ~95 Vitest spec files.
**Run only what your change can break.** `.github/workflows/ci.yml` runs
`dotnet test Proxytrace.sln` on every push, so a local full run buys nothing but wall-clock — and
once a scoped run is green, do not re-run the full suite to "make sure".

| Changed | Run |
|---|---|
| `Proxytrace.<Layer>/**` | `dotnet test Proxytrace.<Layer>.Tests` |
| A domain entity, its EF entity, or a mapping | `dotnet test Proxytrace.Domain.Tests` **and** `Proxytrace.Storage.Tests` |
| A controller, route, or auth handler | `dotnet test Proxytrace.Api.Tests` |
| A service, optimizer, or the run loop | `dotnet test Proxytrace.Application.Tests` |
| One class/area inside a project | `dotnet test <Project> --filter "FullyQualifiedName~<Name>"` |
| Frontend components/hooks | `npm test -- <path-or-pattern>` (from `frontend/`) |
| `Nordstein.Core.Common`, `Nordstein.Core.Domain`, `Nordstein.Core.Testing` (under `core/`) | `dotnet test core/Nordstein.Core.sln` **and** `dotnet test Proxytrace.sln` — Core's tests are not in the product solution |
| DI/module wiring, a shared interface signature, a package bump, a release | `dotnet test Proxytrace.sln` |

Add `--no-restore` (and `--no-build` when nothing changed since the last build) to skip repeat work.

Two things worth remembering:

- The **frontend suite is cheap** — ~1,000 specs in ~3 seconds — so scoping it matters far less than
  on the backend. Scope while iterating, then run bare `npm test` before you call it done. CI runs
  it too (the `frontend` job).
- The e2e and perf suites are **not** routine checks. Both boot Docker stacks and take many minutes;
  run them only when the change is in that flow or when asked (`run-e2e-tests`, `run-perf-tests`).

When reporting results, say which scope you ran — a scoped green run must never be reported as
"all tests pass".

## The harness

The harness itself (`BaseTest<TModule>`) is **Nordstein.Core code**, shared by all Nordstein
products — it lives in `core/Nordstein.Core.Testing` and is documented Core-side in
[`core/docs/testing.md`](../core/docs/testing.md) (which also states the stricter coverage bar
that applies to changes *inside* `core/`). Changing the harness means changing every product's
suite; follow [`core/CLAUDE.md`](../core/CLAUDE.md) for that.

All tests extend `BaseTest<TModule>` (MSTest + AwesomeAssertions):

```csharp
[TestClass]
public class MyTests : BaseTest<Module>
{
    public required TestContext TestContext { get; init; }

    [TestMethod]
    public async Task SomeTest()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<IUser>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();

        IUser entity = await generator.CreateAsync(CancellationToken); // persists
        IUser result = await repo.GetAsync(entity.Id, CancellationToken);

        result.Id.Should().Be(entity.Id);
    }
}
```

- Each test gets an isolated in-memory database (unique name via `Guid.NewGuid()`)
- `CancellationToken` comes from `TestContext.CancellationToken`
- Override `ConfigureContainer(ContainerBuilder)` to customize the DI container for a test class
- Use `generator.GenerateAsync()` for in-memory-only test objects; `CreateAsync()` to persist

**Exception assertions** — use `FluentActions.Invoking(...).Should().ThrowAsync<T>()`:
```csharp
await FluentActions
    .Invoking(() => repo.UpdateAsync(entity, CancellationToken))
    .Should().ThrowAsync<EntityNotFoundException>();
```

## Container-backed tests

A handful of backend tests talk to a **real** service in a throwaway container
(Testcontainers) instead of a mock. Today that is
`Proxytrace.Messaging.Tests/RedisIngestionStreamIntegrationTests.cs`, which round-trips the
Redis Streams ingestion transport through an actual `redis:7-alpine`.

Reach for one only where mocking the client library defeats the purpose of the test. A
substituted `IDatabase` asserts how we *call* a driver and never how the server *replies*, so
it cannot see a wire-format change: the full RESP2→RESP3 switch in StackExchange.Redis 3.x
reshapes the `XINFO GROUPS` and `XAUTOCLAIM` replies our consumer parses, and the mocked suite
passed identically before and after (#523). Reply parsing, protocol negotiation, and
server-side semantics (consumer-group lag, `XAUTOCLAIM` reclaim) are the cases that earn a
container; everything else is cheaper and more precise as a mock.

**How they are gated.** They start their own container, and when no runtime is reachable they
call `Assert.Inconclusive` and report as *skipped* — `dotnet test` must never become a hard
Docker dependency. Setting **`PROXYTRACE_REQUIRE_DOCKER_TESTS=1|true`** inverts that: a startup
failure is then a real failure. CI's `backend` job sets it (see [`ci.md`](ci.md)) so the coverage
can never be lost silently, which is the same class of false-green the tests exist to close.

The guard has to wrap the builder's `Build()` call, not just `StartAsync`: Testcontainers validates
a builder by resolving and pinging the Docker endpoint, so on a machine without a runtime the throw
happens at `Build()` and a `try` that starts one line later never sees it (#526). Construct the
container inside the guarded block and null-check it before disposing on the skip path.

Run them locally like any other test — with Docker up they just run:

```bash
dotnet test Proxytrace.Messaging.Tests --filter "FullyQualifiedName~RedisIngestionStreamIntegrationTests"
PROXYTRACE_REQUIRE_DOCKER_TESTS=1 dotnet test Proxytrace.Messaging.Tests   # fail instead of skip
```

Pin the image to the tag the deployed stack runs (`docker-compose.yml`), keep each test's
container inside the test method — no shared fixture, same isolation rule as everywhere else —
and remember these cost seconds, not milliseconds. They are a targeted supplement to the mocked
tests, not a replacement for them.

### The SSH.NET pin

`Proxytrace.Messaging.Tests` carries a direct `PackageReference` to **SSH.NET 2026.0.0** for a
package no code here calls. It arrives transitively — `Testcontainers.Redis` → `Docker.DotNet` →
`SSH.NET`, because Docker.DotNet can reach a daemon over SSH — and every version at or below
**2025.1.0** carries [CVE-2026-48798](https://github.com/advisories/GHSA-q939-rpr3-3284) (high,
CVSS 7.1): `ScpClient.Download()` does not validate server-supplied filenames on a recursive
download, so a malicious server can traverse out of the target directory.

The exposure is nil — it is a test-only dependency that never ships in a runtime image, and
nothing in this repository downloads over SCP. The *build* breakage was total: NuGet's audit
raises it as `NU1903`, `TreatWarningsAsErrors` promotes it to an error, and the whole solution
build fails, on every branch at once ([#534](https://github.com/NordsteinSoftware/Proxytrace/issues/534)).

`Testcontainers.Redis` 4.13.0 is the newest release, so there was no upstream bump to take. Drop
the pin once one of its releases resolves SSH.NET ≥ 2026.0.0 on its own — check the transitive
graph with `dotnet list package` (including transitives), delete the line, and confirm
`dotnet restore` stays clean without it.

This is the same shape as the manual's Vite override (see
[`commands.md`](commands.md#manual-toolchain-vitepress--the-vite-override)): a pin that exists
only to get ahead of a transitive advisory, and that should be deleted rather than maintained.

## End-to-end tests (Playwright)

The e2e suite (repo-root `e2e/`) boots the full stack via Docker Compose (`docker-compose.e2e.yml`).
**Do not run the e2e tests if Docker is not installed** — they require a working Docker daemon and
will fail without one. Check first (e.g. `docker --version` and `docker info`); if Docker is
unavailable, skip the e2e suite and say so rather than attempting to run it. See the
`run-e2e-tests` skill for how to execute and triage them, and `create-e2e-test` to write them.

In CI (`.github/workflows/e2e.yml`) a failing run uploads two artifacts: `playwright-report`
(always) and `e2e-stack-logs` (on failure only) — per-service Docker Compose logs, container
states, and `docker inspect` output, captured *before* teardown so stack-side failures stay
triageable. The same step also echoes `compose ps -a` plus the **last 200 lines of every service**
into the job log (one collapsed group per service), so a container that crashed or exited can be
diagnosed from the run page without downloading the artifact — which is not always reachable from
wherever the triage happens ([#522](https://github.com/NordsteinSoftware/Proxytrace/issues/522)).

## Prompt behavior (prompt-lab)

Prompts are the one part of the system no assertion can cover: the compiler is a language model, so
a diff tells you nothing until you run it. The **`prompt-lab` skill**
(`.claude/skills/prompt-lab/SKILL.md`) fires scenarios at the live upstream model with an agent's
real prompt and real tool schemas, A/Bs the working copy against the committed version, and writes
transcripts to read.

Use it whenever you change Tracey's system prompt, one of her skills or tool descriptions
(`frontend/src/features/tracey/`), or a sample-client demo agent's prompt. It needs the kiosk LLM
credentials in the repo-root `.env` and makes real (paid) model calls; it needs no Docker, no
database and no login, because Tracey's data tools answer from fixtures. Transcripts land in the
gitignored `.prompt-lab/`.

The fixture world (`.claude/skills/prompt-lab/fixtures/tracey.json`) is declarative JSON that may
**echo the call it answers** — a by-id read returns the entity that was asked for (or `notFound`), a
write reports what was actually posted. That is not a nicety: a fixture that contradicts the model's
own correct call makes it retry until the step budget runs out, and the report then shows a
regression the prompt never caused. The resolver has a self-check —
`node --test .claude/skills/prompt-lab/scripts/fixture-world.test.mjs` — run it after editing the
fixtures.
