# Testing Conventions

**Before writing or modifying any backend test, you MUST invoke the `test` skill
(`.claude/skills/test/SKILL.md`) and follow it.** It is the source of truth for the test
harness: per-test `BaseTest<TModule>` containers, the `ConfigureContainer` / `GetServices`
DI hooks, NSubstitute substitution patterns, and the hard rules against shared
state/fields and `[TestFixture]`-style helper classes. The summary below is orientation
only; the skill overrides it where they differ.

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

## End-to-end tests (Playwright)

The e2e suite (repo-root `e2e/`) boots the full stack via Docker Compose (`docker-compose.e2e.yml`).
**Do not run the e2e tests if Docker is not installed** — they require a working Docker daemon and
will fail without one. Check first (e.g. `docker --version` and `docker info`); if Docker is
unavailable, skip the e2e suite and say so rather than attempting to run it. See the
`run-e2e-tests` skill for how to execute and triage them, and `create-e2e-test` to write them.

In CI (`.github/workflows/e2e.yml`) a failing run uploads two artifacts: `playwright-report`
(always) and `e2e-stack-logs` (on failure only) — per-service Docker Compose logs, container
states, and `docker inspect` output, captured *before* teardown so stack-side failures stay
triageable.

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
