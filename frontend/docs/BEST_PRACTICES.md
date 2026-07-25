# Proxytrace Frontend Best Practices

**Required reading before writing any React/TypeScript in `frontend/`.** This is the source of truth for **code architecture** — how components, data, state, and types are structured.

It is the sibling of [`DESIGN.md`](./DESIGN.md), which owns the **visual system** (tokens, colors, spacing, components-as-pixels). The split is sharp:

- **DESIGN.md** → what it looks like. Read it for anything about color, type scale, spacing, shadows, which UI primitive to render.
- **BEST_PRACTICES.md (this file)** → how it's built. Read it for file structure, hooks, data fetching, state, typing, performance.

When the two touch (e.g. "no inline styles"), DESIGN.md states the visual rule and this file states the code mechanics. Neither overrides the other; they cover different axes.

This document describes the **target state**. Parts of the current codebase may still violate it — those are debt, not precedent. When you touch a file that violates a rule here, leave it at least no worse, and prefer to fix it. Do not copy an anti-pattern just because a neighbor does it.

### Feature deep-dives (read before touching these features)

Some features have their own architecture guide that this file does not duplicate. Read the relevant one **before** changing that feature:

- **Tracey assistant** (`src/features/tracey/`) → [`TRACEY.md`](./TRACEY.md). The chat architecture, the two request planes (reasoning vs. tool/data), progressive tool disclosure + skills, the artifact store, and the rule that tool + prompt definitions live **only** on the client.

---

## 0. The one principle everything else serves

**A component should be small, single-purpose, and obvious at a glance.** Every rule below is downstream of that. If a file takes more than ~30 seconds to understand its shape, it is too big or doing too much.

The repo used to carry several monster files — `features/evaluators/Evaluators.tsx` was once **1432 lines** with 27 nested component functions and 185 inline `style={{}}` blocks; `Dashboard.tsx`, `Providers.tsx`, `Traces.tsx`, and `Setup.tsx` were all far over budget. Those have since been decomposed and now sit under the limits (e.g. `Evaluators.tsx` is ~190 lines orchestrating a `components/` + `hooks/` + `evaluatorMeta.ts` folder). **Don't regress them** — the way they look now is the bar.

The well-structured examples to copy: `api/agents.ts`, `api/query-keys.ts`, `lib/cn.ts`, `components/ui/classes.ts`, `components/ui/ListRail.tsx` (a pure presentational shell — the master/detail left column every view composes; data and selection stay in the page), and feature folders like `features/evaluators/`, `features/runs/`, and `features/playground/` that decompose into `components/`, `hooks/`, `state/`, `lib/`, `drawers/`. Copy *those*. (A master/detail view's left column is now `ListRail` — see DESIGN.md §4; don't re-implement a list header per feature.)

---

## 1. File & component size limits

These are hard limits, not suggestions. CI-grep them if you have to.

| Unit | Soft limit | Hard limit | Action at hard limit |
|------|-----------|-----------|----------------------|
| Any `.tsx`/`.ts` file | 200 lines | **300 lines** | Split before merging |
| A single component function | 80 lines of JSX/logic | **150 lines** | Extract subcomponents |
| Component props | 5 props | **8 props** | Group into an object, or split the component |
| Hook (`use*`) | 60 lines | **120 lines** | Extract helpers / split concerns |

**One exported page component per feature folder; everything it needs lives beside it, not inside it.** When a page grows subcomponents, they become files in a `components/` subfolder, not nested functions. `features/runs/` and `features/playground/` already do this correctly:

```
features/playground/
  Playground.tsx              ← thin orchestrator
  components/                 ← presentational pieces
  state/usePlaygroundSession.ts  ← logic/state
  lib/seed.ts                 ← pure helpers
```

**Rule:** more than 2 component functions in one file → they belong in separate files (exception: a tiny private render-helper used once, < 20 lines).

---

## 2. Feature folder structure (the standard layout)

Every non-trivial route folder under `features/` follows this shape:

```
features/<feature>/
  <Feature>.tsx            # default-exported page, lazy-loaded in App.tsx. Orchestration only.
  components/              # presentational subcomponents (dumb where possible)
  hooks/ or use*.ts        # data + behavior hooks (see §3, §4)
  <feature>.ts             # pure constants, enums, label maps, form initializers
  *.spec.ts                # tests for the pure logic
```

- The page component **wires things together**: reads route params, calls feature hooks, lays out subcomponents. It should contain almost no business logic and no raw `fetch`/`useQuery`.
- Constants, label maps, and `Record<Enum, …>` lookups go in the plain `.ts` file (like `features/evaluators/evaluators.ts`), never inline at the top of a 1400-line component.
- Cross-feature reusable UI → promote to `components/ui/`. Cross-feature logic → `hooks/` or `lib/`. Don't import one feature's internals from another feature.

---

## 3. Data fetching — TanStack Query is the only data layer

There is no `useEffect`-to-fetch, no fetch-in-component, no manual loading booleans. The stack is already correct (`api/client.ts` wrapper, typed `api/*.ts` services, centralized `api/query-keys.ts`). Use it as designed.

### 3.1 Layering

```
component  →  feature query hook (useXxx)  →  api/<service>.ts  →  api/client.ts
```

- **`api/<service>.ts`** — thin typed functions returning DTOs. One per resource (see `api/agents.ts`). No React, no query logic. New endpoints get a function here first.
- **Query hooks** — wrap `useQuery`/`useMutation`. Page/feature components must not call `useQuery`/`useMutation` raw — that couples them to query keys, stale times, and invalidation. The repo now keeps these in `use*` hooks (the raw-in-pages debt that this section once called out has been extracted); keep new ones there too.

```ts
// features/evaluators/hooks/useEvaluators.ts
export function useEvaluators() {
  const { projectId } = useCurrentProject();
  return useQuery({
    queryKey: QUERY_KEYS.evaluators(projectId),
    queryFn: () => evaluatorsApi.list(projectId),
    enabled: !!projectId,
  });
}
```

Components then read `const { data, isLoading } = useEvaluators();` and stay declarative. Stale time, `enabled`, `select`, and placeholder data live in the hook, in one place.

### 3.2 Rules

- **Every query key comes from `QUERY_KEYS`** in `api/query-keys.ts`. Never inline a string array `['evaluators', id]` in a component. (This file is already good — keep it the single registry.)
- **Mutations invalidate, components don't refetch.** After a mutation, `queryClient.invalidateQueries({ queryKey: QUERY_KEYS.xxx })`. Prefer the documented prefix keys (e.g. `testRunGroupsRoot`) for broad invalidation.
- **SSE patches the cache; it does not trigger refetches.** Per DESIGN.md §8 and the `event-stream.ts` broadcasters, live updates use `queryClient.setQueryData` to patch the cached query. Never refetch a page on an SSE event.
- **No data fetching in a component that an SSE broadcaster already streams.** (Also a DESIGN.md anti-pattern.)
- **`enabled` guards dependent queries** — never fetch with an undefined id; gate with `enabled: !!id`.
- **Lists load light DTOs; only the selected/detail item loads the fat DTO.** A list/table query (`useAgents`, `useSuites`, `useTraceQueries`, `useTestRunGroups`) fetches a `…ListItemDto` carrying only the fields the row renders — ids, name, status, run aggregates, and *counts* (`testCaseCount`, `toolCount`, `memberCount`) — never a nested aggregate (`testCases[]`, `tools[]`, `runs[].results[]`, request/response bodies, member lists). The full object is fetched only when an item is selected, via a `useXxxDetail(id)` hook (`enabled: !!id`) keyed by the single-item `QUERY_KEYS` (e.g. `QUERY_KEYS.agent(id)`, `testRunGroup(id)`). The backend mirrors this exactly: the `[HttpGet]` list returns `PagedResult<…ListItemDto>` (a separate mapper method, e.g. `ToListItemDto`); `[HttpGet("{id}")]` returns the fat DTO. This keeps list payloads small and scroll fast. *Exception:* a bulk flow that genuinely needs full data for every row up front (e.g. suite-creation reading request/response across candidate traces, playground replay) uses a dedicated full-list endpoint (`/api/agent-calls/full`), not the light list — those are not display lists. (DB-side read-model projection so the list query also doesn't *load* the fat graph is a separate, future optimization.)
- **Server state ≠ client state.** Anything from the API lives in Query's cache, never copied into `useState`. Copying server data into local state to "edit" it is the most common bug source — derive instead, or use an explicit draft state that's clearly separate.

---

## 4. State management & `useEffect`

`useEffect` is a code smell until proven otherwise. Keep them rare and, where one is genuinely needed (an external subscription/timer/measurement), push it into a dedicated `use*` hook rather than inlining it in a component. Most effects are avoidable — check the table below before reaching for one.

### 4.1 The `useEffect` decision rule

Reach for `useEffect` **only** to synchronize with something *outside* React: DOM events, subscriptions (SSE), timers, focus/measurement, `localStorage`, the document title. For everything else:

| Instead of `useEffect` to… | Do this |
|---|---|
| Fetch data | TanStack Query (§3) |
| Compute a value from props/state | Compute it inline, or `useMemo` if expensive |
| Reset state when a prop changes | `key` prop on the component, or derive |
| Sync two pieces of state | Lift to one source of truth; derive the other |
| Respond to a user event | Do it in the event handler, not an effect |

Acceptable effects already in the repo, for reference: `hooks/useElementWidth.ts` (ResizeObserver), `hooks/useGlobalShortcut.ts` (keydown listener), `api/event-stream.ts` (SSE subscription). These wrap a genuinely external system in a custom hook — that's the right shape. Any effect more than a few lines belongs in its own `use*` hook, not inline in a component.

### 4.2 State placement

- **Local UI state** (open/closed, hovered, active tab) → `useState` in the lowest component that needs it.
- **Server state** → Query cache (§3).
- **Cross-route / app state** (current project, toasts, auth, kiosk) → the existing Contexts (`contexts/ProjectContext`, `ToastContext`, `KioskContext`). Do not add a new global state library; React Context + Query covers this app's needs.
- **Master/detail selection → the URL, via `hooks/useSelectedId.ts`.** Which agent/evaluator/theory/run/suite is selected lives in `?id=` (one shared hook, `{ replace: true }`), not `useState` — so it survives refresh, back/forward, and link sharing. Read the raw param and **derive** the effective default (first item) without writing that default to the URL. This is the standard for every master-detail view; don't reintroduce a path param (`/x/:id`) or per-view `useState` for selection.
- **Derive, don't store.** If a value can be computed from existing state/props, compute it. Don't mirror it into another `useState`.

### 4.3 Forms

Controlled inputs with a single `useState` object for the form, an `initForm()` factory in the feature's `.ts` file (pattern exists in `features/evaluators/evaluators.ts`), and `FormField` for every input (DESIGN.md §7). No uncontrolled `ref` reads for form values.

---

## 5. Component design: presentational vs container

Split components by responsibility:

- **Container** — calls hooks (§3), holds state, handles events. Few of these. Usually the page + a couple of section orchestrators.
- **Presentational** — props in, JSX out. No data fetching, no `QUERY_KEYS`, ideally no `useState` beyond trivial local UI. Easy to read, easy to test, reusable.

The more presentational components you have, the healthier the tree. A 1400-line file is almost always a container that absorbed all its presentational children.

### 5.1 Props

- **Pass data, not styling primitives.** A recurring anti-pattern in `Evaluators.tsx`: passing `color: 'var(--accent-primary)'` strings down through props and into `style={{ color }}`. Instead pass a **semantic** prop (`variant="llm"`, `kind={EvaluatorKind.Agentic}`) and let the leaf map it to a class via DESIGN.md tokens / `lib/colors.ts`. Color decisions belong at the leaf, expressed as classes — not threaded through the tree as CSS-variable strings.
- **Discriminated unions over boolean soup.** `{ status: 'loading' } | { status: 'error', error } | { status: 'ready', data }` beats `isLoading`/`isError`/`data` triplets where they're mutually exclusive.
- **No `any`. No `as any`. No `!` non-null assertion** (the last is also forbidden in CLAUDE.md for backend; hold the same line here). The repo is nearly clean — keep it that way. Type DTOs in `api/models.ts`, narrow with type guards.
- Destructure props in the signature; default values there too (`{ size = 16 }`).

---

## 6. Icons — one source, zero inline SVG

Icons were once the single most duplicated thing in the codebase. That has been consolidated — keep it that way.

**Canonical source:** `components/icons` is the one icon module. Its barrel (`components/icons/index.ts`) re-exports the `Svg` wrapper plus the glyph sets (`directional`, `actions`, `glyphs`). **`lucide-react` is not a dependency** of this project; the earlier DESIGN.md mandate for it was never adopted, and DESIGN.md §5 has been updated to match this reality. Do not add `lucide-react`.

**The rule:**

- **Never declare an `<svg>` icon inside a feature file.** Zero exceptions. An inline `function XIcon()` returning `<svg>` in a feature is a bug. (A genuine data-visualization SVG — a gauge, a chart shape — is not an icon and belongs in `components/charts/` or a feature component; this rule is about *icon glyphs*.)
- **Import every icon from `components/icons`.** If a glyph is missing, add it to the appropriate file there (`glyphs.tsx`/`actions.tsx`/`directional.tsx`) and export it through the barrel — never inline it at the call site.
- Icons take `size`/`className`/`strokeWidth` props and inherit color via `currentColor` (the `Svg` wrapper in `components/icons/Svg.tsx` already does this). Color the icon by setting text color on the parent, never by hardcoding `stroke`/`fill`.

---

## 7. Styling mechanics (code side)

DESIGN.md owns *which* tokens to use. This section owns *how* you apply them in code.

- **`cn()` for all conditional/composed class strings** (`lib/cn.ts`). Never build className with template-string `if` soup.
- **Extract repeated class strings to `components/ui/classes.ts`** when a class recipe is reused (the file already does this for inputs). A class string copy-pasted 3+ times is a missing `classes.ts` entry or a missing component.
- **Inline `style={{}}` only for genuinely runtime-computed values** — a percent width from data, a runtime hex from `colors.ts`. Static values are Tailwind utilities (DESIGN.md §6). The 185 inline styles in `Evaluators.tsx` are almost all static and wrong; do not reproduce that.
- **No new primitive when one exists.** Before writing a button/card/pill/modal, check `components/ui/` and `components/overlays/` (DESIGN.md §3 has the inventory). Reuse beats re-implement.
- **No raw styled control elements.** `<button>`/`<input>`/`<select>`/`<textarea>` are ESLint-forbidden (`no-restricted-syntax`) outside the `components/ui/` primitive layer — render `Button`/`IconButton`/`RowButton`/`Input`/`Select`/`Textarea`/`Checkbox`/`Radio`/`Switch`/`SegmentedControl`/`Combobox`/`Tabs`/`Menu`/`Tooltip` instead. A genuinely bespoke control (range slider, labeled switch-pill, command palette, assistant-ui `asChild` target) is the documented exception: add a one-line `// eslint-disable-next-line no-restricted-syntax -- <reason>` directly above it. Radix-backed widgets (`Tabs`/`Menu`/`Tooltip`/`Combobox`) replace hand-rolled `createPortal` dropdowns.

---

## 8. Performance — measure, don't sprinkle

- **`useMemo`/`useCallback` are not decoration.** Add them only for (a) genuinely expensive computation, or (b) referential stability that a downstream `memo`/dependency array actually needs. Wrapping every value is noise that hides the ones that matter.
- **Stable, meaningful `key`s** on lists — entity ids, never array index for dynamic/reorderable lists.
- **`React.memo` only on presentational leaves that re-render hot** (list rows, chart cells) and receive stable props. Pointless on containers.
- **Don't create new object/array/function literals in props** of memoized children — that defeats the memo. This is the usual reason a `memo` "doesn't work."
- **Long lists must virtualize or paginate.** High-volume scrolling lists (traces, runs) follow the `trace-row`/`DataTable` pattern (DESIGN.md §3.4) and use `Pagination`; don't render thousands of rows.
- **Charts:** reuse `components/charts/*` and `chart-math.ts`. Don't compute SVG paths inline in a feature.

---

## 9. Loading, empty, and error states are not optional

Every async view ships all three (DESIGN.md §3.5 owns the visuals; the code mechanics):

- **Loading:** render `Skeleton` shaped like the final layout (reserve height to prevent layout jump). `Spinner` only for inline/button loading. Drive off Query's `isLoading`/`isPending`.
- **Empty:** `EmptyState` when `data` is empty (distinct from loading). Never show an empty grid as if loaded-with-nothing is the same as loading.
- **Error:** Query's `isError` → inline `text-danger` near the control, or `EmptyState` danger variant for full-page. App-level crashes are caught by `components/ErrorBoundary.tsx` — don't let a render throw blank the app.
- The global `api/client.ts` already surfaces API errors as toasts. Don't double-report; let a mutation's `onError` add context only when it adds value.

### 9.1 Nothing may blank the app

`app/queryClient.ts` sets **`throwOnError: true` globally**, so any unhandled query error is rethrown
during render. That is the right default for a page — the boundary turns it into a message — but it
makes *where* a component renders load-bearing:

- **Every region gets a boundary.** `wrap()` in `app/AppRoutes.tsx` covers route elements, but the
  router `Outlet` is only part of the tree. `Shell` renders the nav rail, the license/update banners
  and the masthead as **siblings** of the `Outlet`, so a route-level boundary structurally cannot
  catch a throw in them — `Shell` wraps each of those regions in its own `ErrorBoundary`. Per-region,
  not one at the root: a root boundary replaces the router too, so the user cannot navigate away
  from the failure. If you add a new region outside the `Outlet`, it needs its own boundary.
- **A query rendered in app chrome sets `throwOnError: false`.** The boundary is a backstop for
  *bugs*; a fetch that fails routinely (the API restarts, a session expires) must degrade in place —
  an empty bell, a hidden badge — not take out the masthead. `features/notifications/hooks/useNotifications.ts`
  is the worked example.
- **Pass `resetKeys={[location.key]}`.** React reconciles one boundary instance across route
  changes, so without it a caught error follows the user to every subsequent page until a full
  reload — and "Try again" alone just re-renders the same children, so a deterministic error returns
  instantly. Use `key`, not `pathname`: several selections live in the query string.
- **Give a chrome boundary a `fallback` shaped like its slot.** The default is a full-height block;
  in a 48px bar it wrecks the layout. `components/layout/ChromeErrorFallback.tsx` is the shared one.

---

## 10. TypeScript & API contracts

- **DTOs live in `api/models.ts`** and are the contract with the backend. Components consume DTO types, never redefine ad-hoc shapes.
- **Enums from the backend** (`EvaluatorKind`, `EvaluationScore`, …) are imported, and exhaustive `Record<Enum, …>` maps get every member (TS will catch a missing key — lean on it).
- `import type { … }` for type-only imports (keeps them out of the JS bundle; the codebase already does this — stay consistent).
- No `any`, no `as any`, no `!`. Narrow with guards. If a type fights you, fix the type, don't escape it.

---

## 11. Accessibility (code mechanics)

DESIGN.md §7 states the requirements; enforce them in code:

- Clickable thing → a real `<button>`/`<a>`/`Button`/`IconButton`. **Never `<div onClick>`** (DESIGN.md anti-pattern).
- Icon-only control → `aria-label`.
- Form input → wrapped in `FormField` (label association handled).
- Keyboard: modals/drawers trap focus + close on Esc — reuse `Modal`/`Drawer`, which already do; don't hand-roll an overlay.
- New keyframe animation → ship its `prefers-reduced-motion` guard in the same change.

---

## 12. Secure programming

A trace/observability tool renders untrusted data: captured prompts, model output, tool arguments, user-supplied names. Treat everything from the API or the network as hostile until proven safe.

### 12.1 Rendering untrusted content

- **Never `dangerouslySetInnerHTML`.** React escapes text by default — keep it that way. Render captured prompts, completions, and tool payloads as text (`<pre>`, `CodeBlock`), not HTML. If markdown rendering is genuinely required, run it through a sanitizer (e.g. DOMPurify) and never feed raw model output to an HTML parser.
- **No `eval`, `new Function`, or `setTimeout`/`setInterval` with a string body.** Ever. Not for "just parsing" JSON — use `JSON.parse`.
- **Validate before `JSON.parse` of network data**, and wrap in try/catch. A malformed trace payload must not blank the app — that's what `ErrorBoundary` (§9) backstops, but parse defensively too.

### 12.2 Links, URLs, and navigation

- **Sanitize any URL that comes from data** before putting it in `href`/`src`. Allow only `http:`/`https:` (and `mailto:` where intended); reject `javascript:`, `data:`, and `vbscript:` schemes — these are stored-XSS vectors when a model or user controls the string.
- **External links carry `rel="noopener noreferrer"`** with `target="_blank"` (prevents reverse-tabnabbing and referrer leakage).
- **Never build a route by string-concatenating untrusted input** into a redirect; route through the router with typed params.

### 12.3 Secrets & sensitive data

- **No secrets in frontend code or the bundle.** API keys, provider tokens, and connection strings live server-side. Anything in the SPA is public — `import.meta.env` values shipped to the client are not secret. The Proxytrace-issued client `ApiKey` is the *only* credential the browser holds, and it's scoped per project/provider by design.
- **Don't log sensitive data.** No `console.log` of tokens, full request bodies, or captured prompt content in production paths. Strip debug logging before merge.
- **Don't persist sensitive data to `localStorage`/`sessionStorage`** (readable by any script). Auth/session handling stays with the existing Context + httpOnly cookies the backend sets; don't hand-roll token storage.

### 12.4 Dependencies & input

- **`npm audit` clean (no high/critical) before a PR.** Don't add a dependency to save 20 lines of code; each one is attack surface and bundle weight.
- **Validate user input at the boundary** (form submit, query param parse) — length caps, expected shape — even though the backend re-validates. Defense in depth; never trust the client *or* the server alone.
- **Respect the same `!`/`any`/`as any` ban (§10):** an unchecked cast that lies about a shape is a security bug waiting to deref `undefined` on attacker-shaped data.

---

## 13. Styling — Tailwind only, no inline styles

DESIGN.md §6 owns *which* tokens; this restates the **hard code rule** because it is the single most violated one (185 inline `style={{}}` blocks in `Evaluators.tsx` alone).

- **Static styles are Tailwind utility classes. Full stop.** Color, spacing, radius, shadow, gradient, layout — all expressed as classes.
- **Complex static values use Tailwind arbitrary-value syntax**, not inline style: `shadow-[var(--shadow-card)]`, `bg-[linear-gradient(...)]`, `rounded-[10px]` — never `style={{ boxShadow: 'var(--shadow-card)' }}`.
- **`style={{}}` is allowed only for genuinely runtime-computed values** — a percent width from data (`style={{ width: ${pct}% }}`), a runtime hex resolved from `lib/colors.ts`. If the value is knowable at author time, it is a class, not a style.
- **Compose conditional classes with `cn()`** (`lib/cn.ts`), never template-string `if` soup. Reused class recipes go to `components/ui/classes.ts` (§7).
- **Don't thread CSS-variable strings through props** (`color="var(--accent-primary)"`). Pass a semantic prop and map to a class at the leaf (§5.1).

(The code mechanics of *how* you apply tokens — `cn()`, `classes.ts`, no new primitives — are in §7; this section is the flat prohibition on inline styles.)

---

## 13a. Internationalization — no hardcoded UI strings

The UI is multilingual (English is the source). **Never ship a bare user-facing string.** Full
guidance — macros, the dev loop, the translation tool, the glossary — is in
[`docs/i18n.md`](../../docs/i18n.md). The rules in brief:

- **Wrap every UI string** with a Lingui macro: `<Trans>` for JSX text, `t\`\`` (from `useLingui()`)
  for `placeholder`/`title`/`aria-label`/other prop strings, `<Plural>` instead of
  `n > 1 ? 's' : ''`, and `msg\`\`` for strings in label maps (resolve with `i18n._()` at render).
- **Keep glossary/technical terms English** (Tool, User, Assistant, Trace, Token, Prompt, Agent,
  Suite, Evaluator, Proposal, …) — the translation tool is told not to translate them.
- After adding labels: `npm run i18n:extract` → `npm run i18n:translate`, then commit
  `src/locales/**`. The `lingui/no-unlocalized-strings` ESLint rule flags raw strings (`warn` during
  migration, `error` once clean).

---

## 14. Testing

### 14.1 Unit tests (Vitest)

- **Pure logic is extracted and unit-tested with Vitest** (`*.spec.ts`). The pattern exists: `features/runs/results.ts` + `results.spec.ts`, `lib/format.ts` + `format.test.ts`, `features/setup/Setup.spec.ts`. Computation living in a 1400-line component is computation that can't be tested — extract it to a `.ts` file, then test it.
- Test the **logic** (selectors, formatters, reducers, derivations), not the framework. Don't snapshot-test whole pages.
- A bug fix in pure logic gets a failing test first.
- `npm test` (Vitest) and `npm run build` (tsc typecheck) and `npm run lint` must pass before a PR. Run `npm run lint` frequently during work (CLAUDE.md).

### 14.2 E2E tests (Playwright)

E2E tests live in `e2e/` at the repo root and run against the full Docker Compose stack. They are the right tool for flows that cross a process boundary (browser → API → DB, or proxy → Redis → UI). For component rendering and pure logic, use unit tests.

**When a new feature requires an E2E test:**

| What changed | Where to add it |
|--------------|-----------------|
| New top-level route | Add to `ROUTES` array in `e2e/tests/smoke.spec.ts` |
| New CRUD entity or admin flow | Add `test.describe` block to `e2e/tests/core-crud.spec.ts` or a new `core`-project spec |
| Flow through the ingestion proxy | `e2e/tests/ingestion.spec.ts` (tag `@llm`, gate with `test.skip`) |
| Flow requiring real LLM output | New spec in `e2e/tests/`, matched by the `llm` project in `playwright.config.ts` |

Read `e2e/GUIDE.md` before writing your first spec. It covers selectors, auth, polling, and debugging.

### 14.3 Write components for testability (`data-testid`)

**Every interactive element and every named list/container must carry a `data-testid` attribute.** E2E tests break when they rely on text content (text changes) or CSS classes (refactors). `data-testid` is a stable contract.

**What needs `data-testid`:**

- Primary action buttons: `data-testid="<entity>-create-btn"`, `"<entity>-save-btn"`, `"<entity>-delete-btn"`
- Per-row action triggers: `data-testid={\`<entity>-edit-btn-${id}\`}`
- List/table containers: `data-testid="<entity>-list"`
- Individual rows/cards in dynamic lists: `data-testid={\`<entity>-row-${id}\`}`
- Empty state: `data-testid="<entity>-empty-state"`
- Key display fields (name, status, value): `data-testid="<entity>-name"`, `"<entity>-status"`

**Convention:** `<entity>-<element-type>[-<id>]` — all lowercase, hyphenated.

```tsx
// ✅ correct
<button data-testid="provider-create-btn" onClick={onCreate}>
  New Provider
</button>

<ul data-testid="provider-list">
  {providers.map((p) => (
    <li key={p.id} data-testid={`provider-row-${p.id}`}>
      <span data-testid={`provider-name-${p.id}`}>{p.name}</span>
      <button data-testid={`provider-delete-btn-${p.id}`} onClick={() => onDelete(p.id)}>
        Delete
      </button>
    </li>
  ))}
</ul>

// ❌ wrong — no anchor for tests; text changes break automation
<button onClick={onCreate}>New Provider</button>
<li key={p.id}>{p.name}</li>
```

**Selector priority in tests** (documented in `e2e/GUIDE.md`):
1. `page.getByTestId('...')` — preferred; survives text and style changes
2. `page.getByRole('button', { name: '...' })` — for labelled interactive elements
3. `page.getByText('...')` — only for asserting content is present, never for clicking

---

## 15. Naming & imports

- Components `PascalCase`, hooks `useCamelCase`, utilities/constants `camelCase`, files match their default export (`AgentDetail.tsx` exports `AgentDetail`).
- Boolean props/state read as predicates: `isLoading`, `hasError`, `canEdit`.
- Don't import across feature boundaries (`features/a` importing `features/b/components/...`). Shared code moves up to `components/`, `hooks/`, or `lib/`.
- Keep import groups ordered: external → `api`/`lib`/`hooks` → `components` → local. (The codebase loosely follows this; ESLint can enforce it.)

---

## 16. Pre-merge checklist (code)

Pair this with DESIGN.md §10 (visual checklist). Both must pass.

- [ ] No file over 300 lines; no component function over 150.
- [ ] No more than 2 component functions per file.
- [ ] Page component is orchestration only — no raw `useQuery`/`fetch`, no inline business logic.
- [ ] `useQuery`/`useMutation` live in a `use*` hook, not inline in the page.
- [ ] Every query key comes from `QUERY_KEYS`; mutations invalidate, don't refetch.
- [ ] No `useEffect` that should be Query, a derivation, an event handler, or a `key` reset.
- [ ] No server data copied into `useState`.
- [ ] No inline `<svg>` icon — imported from the one icon module.
- [ ] No static value in `style={{}}`; `cn()` + Tailwind / `classes.ts` instead.
- [ ] No `dangerouslySetInnerHTML`, `eval`, or `new Function`; untrusted content rendered as text.
- [ ] Data-derived URLs scheme-checked; external links have `rel="noopener noreferrer"`.
- [ ] No secrets/tokens in bundle, logs, or `localStorage`; `npm audit` clean (no high/critical).
- [ ] No `any`, no `as any`, no `!`.
- [ ] Loading + empty + error states all present for async views.
- [ ] Clickable = real button/anchor; icon-only has `aria-label`.
- [ ] No raw `<button>`/`<input>`/`<select>`/`<textarea>` outside `components/ui/` — a primitive is used (or a justified `// eslint-disable-next-line no-restricted-syntax`).
- [ ] Pure logic extracted to a `.ts` file and unit-tested.
- [ ] New routes added to `ROUTES` in `e2e/tests/smoke.spec.ts`.
- [ ] New CRUD flows covered by an E2E test in `e2e/tests/`.
- [ ] Interactive elements and list rows carry `data-testid` per §14.3 convention.
- [ ] `npm run build`, `npm run lint`, `npm test` all green.

---

## 17. When you touch a debt file

You will open files that violate this guide (`Evaluators.tsx` chief among them). The rule:

1. **Don't make it worse.** No new inline SVG, no new 200-line addition, no new `style={{}}`.
2. **Leave a clean seam.** If you add a subcomponent, put it in `components/` as a new file, not as function #28 in the monster.
3. **Opportunistically extract** the slice you're working in — pull its logic to a hook or `.ts`, its render to a component file. Boy-scout it.
4. Large rewrites of a debt file are a **tracked refactor** (see the repo's `REFACTORING-TODO.md` flow), not a drive-by inside an unrelated feature PR.

Drift in unowned files is the enemy (DESIGN.md §11 says the same for tokens). The way out of a messy frontend is a thousand small correct decisions, enforced here.
