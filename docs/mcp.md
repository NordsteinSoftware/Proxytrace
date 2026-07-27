# MCP Server

Proxytrace hosts an in-process [Model Context Protocol](https://modelcontextprotocol.io) server so
external agents can drive Proxytrace functionality, decoupled from the browser-only Tracey assistant.
This page covers the backend design; the user/integrator guide is `manual/guide/mcp-server.md`.

## Shape & decisions

- **Decoupled from Tracey.** Tracey's tools are browser closures (Vercel AI SDK `tool()` + IndexedDB
  artifact store + navigate/confirm/render). They are not server-portable. The MCP server has its own
  native C# tool layer that calls the **same Application services the REST controllers use**.
- **Authenticated by `IApiKey`, gated by scope.** The MCP credential is an existing Proxytrace API key
  (minted on the Providers page) — there is no separate MCP token. The key's `Project` is the request
  context.
- **Scopes (`ApiKeyScopes`, least privilege).** A key carries a flags set: `Ingestion`, `McpRead`,
  `McpWrite`, `ApiRead`, `ApiWrite`. The ingestion proxy requires `Ingestion`; the MCP server requires
  `McpRead`, and its write tools additionally require `McpWrite`; the REST API (`/api/*`) accepts a key
  with `ApiRead` for safe requests and additionally requires `ApiWrite` for mutations (see
  [REST API keys](#rest-api-keys-api)). Keys are **not** interchangeable across surfaces unless
  explicitly granted the matching scope — an ingestion key cannot drive MCP or REST, an MCP key cannot
  proxy LLM traffic or drive REST, and a REST key cannot drive MCP. Existing/legacy keys default to
  `Ingestion` only (the `AddApiKeyScopes` migration backfills them); the REST scopes add no schema
  change (new flag values on the existing `int` column), and no key silently gains any new power.
- **Per-project *and* per-user.** The key carries a required `IUser Owner` and an `IProject Project`.
  A call is scoped to the project (its tools see only that project) and **attributed to the owner**:
  the auth handler stashes the owner as the current user, so `ICurrentUserAccessor` resolves them inside
  the tools exactly as a JWT request would. The owner is chosen when the key is minted (an explicit
  user, else the creating admin).
- **Ungated.** MCP itself is not license-gated, consistent with `IApiKey`. (Proposal/theory tools still
  respect `LicenseFeature.OptimizationProposals`, like their REST controllers.)

## Hosting

`ModelContextProtocol.AspNetCore` (Streamable HTTP, **stateless**), wired in
`Proxytrace.Api/Module.cs` and mapped in `Program.cs`:

```csharp
// Module.cs (registered unconditionally — inert until the endpoint is mapped)
services.AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithToolsFromAssembly(typeof(Module).Assembly);

// Program.cs (non-kiosk only)
app.MapMcp("/mcp").RequireAuthorization("Mcp");
```

Stateless transport means every JSON-RPC POST is its own request: it re-runs the auth handler, so the
project context is re-established per call and tools execute within that request's `HttpContext`. **What
disables MCP in kiosk is the endpoint, not the DI registration:** the MCP server services, the tool
types and the `McpApiKey` scheme are registered unconditionally (they are inert until used), but
`Program.cs` only maps `/mcp` when `!Kiosk.Enabled`, and `KioskReadOnlyMiddleware` 403s the POST
anyway. Decoupling registration from the kiosk flag keeps it deterministic regardless of the ambient
config the `Module` reads (it pulls `appsettings.local.json`, which the host's `IConfiguration` does
not), so `McpServerEndpointTests` exercises the same stack production maps.

**Reverse proxy.** `/mcp` is served by the API but reached at the app's public origin, so it must be
proxied through alongside `/api`. The bundled `frontend/nginx.conf` forwards `/mcp` → `api:8080` with
`proxy_buffering off` (Streamable HTTP may stream), and `frontend/vite.config.ts` proxies `/mcp` →
`localhost:5001` in dev. A custom reverse proxy in front of Proxytrace must forward `/mcp` the same
way. The UI therefore advertises `${window.location.origin}/mcp` (same-origin), and external MCP
clients connect there with a key as the bearer token.

## Authentication & project context

- `McpApiKeyAuthenticationHandler` (`Proxytrace.Api/Auth/Mcp/`, scheme `"McpApiKey"`): reads
  `Authorization: Bearer <key>`, resolves it via `IApiKeyRepository.FindByKeyAsync`, **rejects keys
  without the `McpRead` scope**, then stashes `apiKey.Project.Id` and `apiKey.Scopes` on
  `HttpContext.Items` (`ProjectIdItemKey` / `ScopesItemKey`), **and the owner id under
  `CurrentUserAccessor.UserIdItemKey`** so `ICurrentUserAccessor` attributes the call to the owner. The
  ingestion proxy enforces the mirror image — `ApiKeyResolver` rejects keys without `Ingestion`.
- **Write tools call `IMcpProjectAccessor.RequireWriteScope()`** first, which throws `McpException`
  unless the key was granted `McpWrite`. So a read-only (`McpRead`-only) key can list/get everything
  but cannot curate suites, start/cancel runs, or change proposal status.
- The `"Mcp"` authorization policy (`Program.cs`) pins `/mcp` to **only** the `McpApiKey` scheme, so a
  browser JWT/cookie can't reach it and an API key isn't valid elsewhere. The default scheme stays
  JwtBearer — existing `[Authorize]` controllers are untouched.
- `IMcpProjectAccessor` / `McpProjectAccessor` (`Proxytrace.Api/Mcp/`) resolves the ambient `IProject`
  for the request from the stashed id. Every tool begins with `await project.GetProjectAsync(ct)` and
  scopes its work to `project.Id`, rejecting cross-project ids with `McpException`.

## REST API keys (`/api`)

The same `IApiKey` credential can also drive the **REST API**, so a machine caller no longer needs a
long-lived service-user JWT (with MFA disabled and a refresh loop) to hit `/api/*`. This is a separate
scope pair from MCP, kept least-privilege:

- `ApiKeyAuthenticationHandler` (`Proxytrace.Api/Auth/Rest/`, scheme `"ApiKey"`): reads
  `Authorization: Bearer <key>`, short-circuits any non-`proxytrace-` bearer straight back to JwtBearer
  **before any DB lookup** (so browser/OIDC JWT traffic pays nothing), resolves the key via
  `IApiKeyRepository.FindByKeyAsync`, and **rejects keys without a REST scope** (`ApiRead`/`ApiWrite`).
  Like the MCP handler it stashes the owner id so `ICurrentUserAccessor` attributes the call to the
  owner, and the key id under the same item key the audit actor accessor reads — attribution comes for
  free (`AuditActorType.ApiKey`).
- **The key is confined to its own project**, and this is load-bearing, not cosmetic. The handler
  stashes `apiKey.Project.Id` under `ApiKeyAuthenticationHandler.ProjectIdItemKey`, and
  `ProjectAccessGuard` intersects **every** access decision with it — `CanAccessProjectAsync` denies a
  different project outright, and `GetAccessibleProjectIdsAsync` collapses the admin "all projects"
  `null` to just that one id. Without this the key acts purely as its owner, and because
  `POST /api/providers/{id}/keys` is admin-only the owner is normally an **Admin**, whose role alone
  satisfies the guard for every project in the instance — so a key minted for one project could read
  and (with `ApiWrite`) mutate all of them, contradicting the contract the UI and
  `manual/admin/providers-and-api-keys.md` advertise. A claim is **not** sufficient here: the
  `"project"` claim these handlers used to emit was read by nothing. Pin new scoping on the
  `HttpContext.Items` entry the guard actually consults, exactly as MCP does with
  `McpProjectAccessor.ProjectIdItemKey`.
- The **default** authorization policy (`Module.ConfigureAuth`, non-kiosk) accepts the `ApiKey` scheme
  **alongside** JwtBearer and carries an `ApiKeyScopeRequirement`. `ApiKeyScopeHandler` enforces the
  read/write split **by HTTP method** for API-key callers only: safe methods (`GET`/`HEAD`/`OPTIONS`)
  require `ApiRead`, mutations (`POST`/`PUT`/`PATCH`/`DELETE`) require `ApiWrite`. Interactive JWT/cookie
  callers are governed by roles exactly as before — the requirement never constrains them.
- **Admin endpoints stay JWT-only.** `[Authorize(Roles = Admin)]` requires a role claim an API key never
  carries, so a scoped key is denied (403/401) on every admin action — a strictly better posture than a
  full-permission service-user JWT.
- Kiosk builds don't register the scheme (kiosk is read-only and login-free), so this is a non-kiosk
  affordance only.

## Tools

Tool classes live in `Proxytrace.Api/Mcp/Tools/`, each `[McpServerToolType]`, injecting the same
Application services/repositories/DTO mappers the controllers use and returning full JSON (no artifact
store, no digests). Adding a tool: add a `[McpServerTool]`-attributed method on a `[McpServerToolType]`
class in that folder — `WithToolsFromAssembly` discovers it; the type is registered in Autofac by the
reflection scan in `Module.cs`. Current surface: agents, traces, suites (+curation — **`add_trace_to_suite`
takes an optional `expectedOutput` to record a human *correction*, not just promote a trace as-is**),
runs (+start/cancel, **`get_run_failures`**, **`compare_runs`**), proposals (+status, gated), theories
(gated, read **+ `submit_theory`**), statistics (**+ `get_agent_overview`**). See
`manual/guide/mcp-server.md` for the full tool list.

The correction seam (`add_trace_to_suite` with `expectedOutput`) maps onto the same `ITestCase`
correction factory the REST `POST /api/test-suites/{id}/test-cases` endpoint uses, so an MCP client can
now record "the agent saw this input, and the right answer was X" — turning a rejected output into a
regression test — and drive the whole optimization loop with a single scoped MCP key. Every promoted or
corrected case keeps a `SourceAgentCallId` link back to the trace it came from.

## Prompts (workflows)

The server also exposes MCP **prompts** — reusable workflows an MCP client surfaces (e.g. as slash
commands). A prompt returns a playbook that drives the model through the tools in the right order; they
mirror the Tracey assistant's skills, retargeted to the MCP tool surface (no cards/confirmation/await).
They are **static** `[McpServerPrompt]` methods on `[McpServerPromptType]` classes in
`Proxytrace.Api/Mcp/Prompts/`, registered via `WithPromptsFromAssembly` and served over the same
`/mcp` endpoint. Current set: `optimize_agent`, `curate_suite`, `run_tests`, `review_proposals`,
`project_insights`. Adding one: add a `[McpServerPrompt]`-attributed static method returning the
playbook string.

## Tests

`Proxytrace.Api.Tests/Mcp/` covers the auth handler (valid/unknown/missing key, and **two keys →
two distinct project contexts**), the project accessor, and a representative tool's project scoping.
`McpServerEndpointTests` is the end-to-end guard: a `TestServer` host wired like production (in-memory
storage, the `McpApiKey` scheme, the `"Mcp"` policy, stateless Streamable HTTP) driven by the real MCP
client — it proves that a tool, run in the SDK's stateless request scope, sees the project the API key
resolved to on that request (and that an unknown key is rejected by the policy). Scope coverage:
`ApiKeyResolverTests` rejects a non-`Ingestion` key at the proxy; the auth-handler tests reject an
`Ingestion`-only key at `/mcp`; the accessor tests cover `RequireWriteScope` with and without `McpWrite`.
