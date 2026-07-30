---
name: manual-screenshots
description: Capture and embed screenshots into the Proxytrace user/operator manual (manual/guide/*, manual/admin/*). Use whenever a manual page needs a screenshot or an existing manual image must be refreshed — e.g. "add a screenshot to the dashboard guide", "the agents manual page needs images", "show the test-run UI in the docs", "illustrate this manual page", "update/refresh the manual screenshots". Boots the docker-compose.kiosk.yml stack (self-seeded, login-free), captures plain dark shots with Playwright, embeds them, verifies the VitePress build, and tears the stack down. Docker required.
---

# Manual Screenshots

Capture plain, dark, on-brand screenshots for one Proxytrace manual page (`manual/guide/*` or
`manual/admin/*`) from the self-seeded, login-free kiosk stack, embed them, verify the VitePress
build, then tear the stack down. **Per-page helper** — capture only what the page you're writing
needs; this is not a batch "refresh the whole manual" pipeline.

Outstanding pages and suggested shots are tracked in `manual/screenshots/screenshot_todo.md` — tick
items off and keep it current as you capture.

## Prerequisites / guardrails

- **Docker required.** If `docker info` fails, stop and tell the user — there is no fallback.
- The kiosk stack is isolated: compose project `proxytrace-kiosk`, **in-memory** storage, ports
  `5200`/`5201`/`5202`. It cannot reach the user's `proxytrace` DB or app ports. Still — only
  ever manage the kiosk compose; never `down` other compose projects.
- **Plain screenshots only** — no annotations, arrows, or callouts.
- **Always tear the stack down** at the end, including on failure.
- Manual/docs-only change ⇒ **no `CHANGELOG.md` entry** (changelog is for product changes). Keeping
  `manual/` current is still required.

## Steps

### 1. Pick the shot list
Open the manual page. List the *minimum* screenshots that illustrate the prose (e.g. the list view,
the primary create/edit dialog, one detail view). Fewer is better.

**Skip pages the kiosk can't represent** — login/MFA, user management, invites, and deployment/ops
pages with no product UI: the kiosk has no sign-in and exactly one seeded user. Leave those without
screenshots or handle case-by-case. `/settings/*` *is* reachable — the demo user is an administrator
and the kiosk runs on an Enterprise override license, so admin-only and licensed surfaces (settings,
cost budgets, Tracey) render fully rather than locked.

### 2. Preflight
- `docker info` succeeds (else stop).
- Manual deps present for the later build: if `manual/node_modules` is missing, `cd manual && npm install`.

### 3. Boot the kiosk stack
```bash
docker compose -f docker-compose.kiosk.yml up --build -d
```
Wait until ready (cold .NET start + seeding ~20s): poll `http://localhost:5200/api/health` for
`{"status":"ok"}` — `capture-lib.mjs` exports `waitForReady()` for exactly this. The seeded,
login-free app is served at `http://localhost:5201`. Discover the route you need by navigating that
app (or check `frontend/src` routing).

### 4. Capture
Write a short temp capture script that imports the shared helpers and drives each state, then run it
**from `e2e/`** so Playwright resolves from `e2e/node_modules`:
```bash
(cd e2e && node ../manual/screenshots/_capture.mjs)
```
Template:
```js
import { launch, goto, shot, waitForReady, outDir } from '../manual/screenshots/capture-lib.mjs';
import { join } from 'node:path';

await waitForReady();
const { browser, page } = await launch();
const dir = outDir('dashboard');                       // -> manual/public/screenshots/dashboard/

// Always pass a real selector that exists on the page (inspect the running app first).
await goto(page, '/dashboard', 'main');
await shot(page, join(dir, 'overview.png'));           // full page

// Component close-up:
// await shot(page, join(dir, 'cost-card.png'), { selector: '<a real selector>' });

await browser.close();
```
- Captures land in `manual/public/screenshots/<page-slug>/<shot>.png`.
- Take **all** of one page's shots in a single run — the kiosk re-seeds per boot, so one session
  keeps a page's images mutually consistent.
- **Delete the temp `_capture.mjs`** afterward.

### 5. Embed in the markdown
Reference with a root-absolute path (VitePress prefixes the `/docs/` base at build):
```md
![Descriptive alt text](/screenshots/<page-slug>/<shot>.png)
```
Write real alt text describing what's shown.

### 6. Verify the build
```bash
cd manual && npm run docs:build
```
Build must pass and the images must resolve. Preview live with `npm run docs:dev`
(http://localhost:4202).

### 7. Tear down (always)
```bash
docker compose -f docker-compose.kiosk.yml down
```
Then delete the temp capture script and confirm only intended PNGs were added.

## Look & conventions (handled by `manual/screenshots/capture-lib.mjs`)
Dark theme (the app is dark by default, matching the manual's `appearance: 'force-dark'`), 1280×900
at `deviceScaleFactor: 2` (crisp/retina), reduced motion, and a `document.fonts.ready` settle before
each shot. Pass `{ selector }` to `shot()` for a cropped component shot. `goto()` uses
`domcontentloaded` (not `networkidle` — traces/runs hold open SSE connections), so always pass it a
concrete `waitFor` selector.

**Inner scroll:** most app pages scroll inside a container, not the document, so `fullPage` captures
only the viewport (a clean top-fold "hero" — often what you want). To fit more of a long page in one
shot, raise the viewport height: `launch({ height: 1600 })`.

## Not in scope
Annotations/callouts, a global screenshot manifest or batch refresh, non-Docker capture, and
screenshots of auth / user-management / licensing-admin UI.
