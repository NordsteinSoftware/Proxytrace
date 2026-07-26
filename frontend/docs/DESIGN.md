# Proxytrace UI Design Guide

**Required reading before any frontend work.** This document is the source of truth for visual style, component conventions, and interaction patterns. Skim if you've read it before; obey it always. The codebase already implements these rules — your job is to extend the system, not invent a parallel one.

If a rule here conflicts with a generic design recommendation (from a tool, agent, or external skill), this guide wins.

The system has a name: **Signal Desk ("Wire")** — a flat, ruled instrument surface in blue-petrol ink with a single signal-cyan accent.

---

## 1. Product context

Proxytrace is an AI-agent observability + benchmarking platform. The user is a developer or ML engineer staring at traces, test runs, evaluations, and proposals — often for long stretches. That dictates everything below:

- **Information density over whitespace luxury.** Body text is 12px, not 16px. Panes stack tightly. Whitespace earns its place.
- **Flat instrument, not floating cards.** The lineage is a terminal / an instrument panel, not a marketing surface. Structure comes from **1px rules**, not from shadows, gutters, or rounded floating panels. Corners are square. Fills are one flat color. The accent is **cyan**, not gold.
- **Code-adjacent feel.** Mono is structural, not decorative: IDs, payloads, model names, JSON, the topbar breadcrumb, nav page codes, table headers, KPI labels. Inter for prose.
- **Trust signals matter.** Status colors must be unambiguous. Number formatting must be consistent. Streaming/live state must be visible without being noisy.

Anti-personas: marketing landing page, e-commerce product, consumer app. Do not import patterns from those domains.

---

## 2. Theme system — design tokens

All tokens are declared in `frontend/src/index.css` and exposed to Tailwind 4 via `@theme`. **Never hardcode hex values, pixel sizes, or shadow strings in components.** Reference tokens through Tailwind utilities (`bg-card`, `text-secondary`, `rounded-lg`, `text-body-sm`) or arbitrary-value syntax against the CSS variable (`shadow-[var(--shadow-card)]`, `bg-accent`).

### 2.1 Colors

Dark-only — blue-petrol ink panes divided by opaque steel rules, with a single signal-cyan accent.

| Role | Token | Value | Use |
|------|-------|-------|-----|
| Page background | `bg-surface` | `#0a0f14` | Body / app shell |
| Elevated surface | `bg-surface-2` | `#0d141b` | Topbar masthead, modal panel, `Card elevation="floating"` |
| Sidebar | `bg-sidebar` | `#0d141b` | Left nav rail |
| Card | `bg-card` | `#101a23` | Default card / pane |
| Card raised | `bg-card-2` | `#152230` | Nested panel, input field, hovered list row |
| Border | `border-border` | `#1e2b38` | Default rule — pane dividers, masthead/rail edges |
| Border subtle | `border-border-subtle` | `#16212c` | Row dividers |
| Hairline | `border-hairline` | `#182430` | Internal card splits |
| Text primary | `text-primary` | `#d5e1ea` | Headings, key values |
| Text secondary | `text-secondary` | `#8ba0b2` | Labels, eyebrows, body prose |
| Text muted | `text-muted` | `#5d7488` | Captions, placeholders, inactive glyphs |
| Accent | `text-accent`, `bg-accent` | `#57c4d3` | Primary CTAs, active nav, focus ring |
| Accent hover | `accent-hover` | `#7dd3e0` | Hover only |
| Accent press | `var(--accent-press)` | `#45aebc` | Pressed/active cyan fill |
| Accent ink | `text-accent-ink` | `#062229` | Dark text/icon **on** a cyan fill (primary button) — 8:1 on accent |
| Accent text | `text-accent-text` | `#a8e2ec` | Cyan text on dark (active filter chip / status) |
| Accent subtle | `bg-accent-subtle` | `rgba(87,196,211,0.13)` | Tinted accent surface |
| Accent border | `var(--accent-border)` | `rgba(87,196,211,0.45)` | Cyan hairline (active chip / status) |
| Success | `text-success`, `bg-success` | `#5aba80` | Pass, healthy, run-green |
| Success ink | `text-success-ink` | `#05220f` | Dark text/icon **on** a green fill — 7.06:1 on success |
| Warn | `text-warn` | `#d9a23f` | Throttle, slow, 4xx — amber lives **here only**, never as chrome |
| Danger | `text-danger` | `#dd5959` | Fail, error, destructive |
| Danger ink | `text-danger-ink` | `#290606` | Dark text/icon **on** a red fill — 5.03:1 on danger |
| Teal | `text-teal` | `#7d95c9` | Steel-blue: rule-based evaluators, info chips *(the token name is historical — the value is steel-blue, not teal)* |

**No background atmosphere.** The surface is a flat instrument pane: there are no `body::before` / `body::after` aurora or film-grain layers, no page-level radial washes, no per-page background effects. `Shell` and its `<main>` are `bg-transparent` and sit directly on the body color.

**Subtle semantic backgrounds** (`bg-success-subtle`, `bg-warn-subtle`, `bg-danger-subtle`, `bg-accent-subtle`) — use these for status tags and tinted surfaces. Never put white text on a subtle background; pair the subtle bg with the matching solid text color.

**On-fill foregrounds use the `-ink` family.** Text or icons sitting *on* a solid semantic fill take `text-accent-ink` (on `bg-accent`), `text-danger-ink` (on `bg-danger`), or `text-success-ink` (on `bg-success`). **Never `text-white` on a colored fill** — the ink tokens are the AA-verified pairings and exist precisely so nobody has to reach for white. There is deliberately no amber ink token, because nothing solid-fills `--warn`; if that ever changes, add one to `index.css` and `@theme` rather than improvising a foreground.

The same rule covers **categorical entity fills** — an `Avatar`/`AgentAvatar` filled with a runtime
`agentColor()` / `modelColor()` / `providerColor()`. `text-accent-ink` is the sanctioned foreground
there too: it clears AA against every entry in the entity palette (worst case, rose `#d3737f`, at
5.15:1), which no light foreground does. Any colour added to the palette in `lib/colors.ts` must
hold that pairing.

**Dynamic colors** (per-model, per-agent, per-provider, per-evaluator) come from `frontend/src/lib/colors.ts`: `modelColor(name)`, `agentColor(id)`, `providerColor(name)`, `projectColor(id)`, `detectorColor(id)`, `evaluatorColor(kind)` / `EVALUATOR_KIND_COLOR`, `statusColor(httpStatus)`. Hash-based assignment is stable — do not invent new palettes for new entity types; extend the existing helpers. The per-entity **categorical** palette (`AGENT_PALETTE` in that file) is the one sanctioned exception to "no new hexes": charts, legends, and badge dots need more *mutually distinct* hues than the ~5 semantic tokens supply, so it is derived from the Wire anchors (accent cyan `#57c4d3`, warn amber `#d9a23f`, success green `#5aba80`) by holding a muted saturation/lightness band and rotating hue to fill the gaps. Every entry clears AA (≥ 4.5:1) as text on the ink surfaces. That is for data-encoding only — never reach into it for chrome, CTAs, or semantic status. `MODEL_PALETTE` deliberately *groups* instead (all gpt cyan, all mini amber, all claude green) and may repeat.

`tint(color, pct)` (same file) mixes a **runtime** color toward transparent. For a static token base, use a Tailwind arbitrary class (`bg-[color-mix(in_srgb,var(--accent-primary)_14%,transparent)]`) instead.

**Never** introduce new brand hexes for chrome or semantics. If you think you need one, you don't — combine accent + a semantic + opacity. (The categorical entity palette above is the single, scoped exception, and only for per-entity data encoding.)

### 2.2 Typography

- **Body:** Inter, with feature-settings `cv11 ss01 ss03` (defined globally on `body`).
- **Mono:** JetBrains Mono (fallback Fira Code) — apply via `.mono` class or Tailwind `font-mono`. Use for IDs, model names, token counts, JSON, code, kbd hints, the topbar breadcrumb, nav page codes, and every eyebrow label.
- Both faces are **bundled** via `@fontsource-variable/inter` and `@fontsource-variable/jetbrains-mono` (imported in `main.tsx`; family names `'Inter Variable'` / `'JetBrains Mono Variable'`). Never add a Google Fonts `<link>` — CSP allows `font-src 'self'` only.
- **Heading font ≠ body font** is *not* our pattern. We use Inter for prose everywhere; rhythm comes from the type scale and from the Inter/mono contrast, not from a third face.

Type scale (data-dense — do not enlarge without a strong reason):

| Token | Size | Use |
|-------|------|-----|
| `text-caption` | 10px | Eyebrow labels, nav page codes, kbd, axis ticks |
| `text-body-sm` | 11px | Tags, chips, secondary labels, metadata |
| `text-body` | 12px | Default body, table cells, descriptions |
| `text-title` | 13px | Card section labels, button labels, nav items, breadcrumb |
| `text-h2` | 14px | Card titles, rail titles |
| `text-h1` | 18px | Page titles, drawer headers |
| `text-display-sm` | 22px | Secondary KPI figures (stat cells/tiles), setup wizard step headings |
| `text-display` | 28px | Hero KPI numbers |
| `text-chat` | 15px | **Tracey chat prose only** (§8.2) — messages, composer, in-chat h3 |
| `text-chat-title` | 16px | **Tracey chat only** (§8.2) — in-chat markdown h2 |

Weights: 400 (default), 500 (nav, secondary buttons), 600 (titles, primary buttons, KPI), 700 (rare — model tags already at 600). Never use 800/900.

Line height: default; never override globally. For multi-line prose blocks (rare), `leading-relaxed`.

**Eyebrow labels — `EYEBROW_CLS`.** The Wire system's structural label treatment: mono, uppercase, letter-tracked. It is exported once from `components/ui/classes.ts`:

```ts
export const EYEBROW_CLS = cn('font-mono text-caption uppercase tracking-[0.14em] text-secondary');
```

Use it for **table/column headers** (`DataTable` already applies it), **KPI card labels** (`KpiCard`), and **rail counts** (`RailHeader`) — anywhere a small label names a column, a metric, or a section. Do not hand-roll `text-caption uppercase font-mono` again; import the constant.

Two rules about it, both load-bearing:

- **It is `text-secondary`, not `text-muted`.** `text-muted` measures ~3.6:1 on `bg-card` and fails WCAG AA for this size; `text-secondary` measures ~6.5:1. Do not "tone it down".
- **Import it, don't copy it.** It is `SCREAMING_CASE` where its neighbours in `classes.ts` are `camelCase` (`fieldLabelCls`, `kbdCls`) — that is the name the system uses; don't rename it. And don't add a third copy: the dashboard tier already carries its own local `EYEBROW_CLS` / `COL_HEADER_CLS` in `features/dashboard/dashboardMeta.ts`. New and shared code imports the one from `components/ui/classes.ts`.

`fieldLabelCls` (also in `classes.ts`) stays the **form-field** label — Inter, uppercase, semibold, `text-secondary`, applied by `Label`/`FormField`. It lands on `text-secondary` for the same contrast reason as the eyebrow; don't tone it to `text-muted` either. Eyebrow ≠ field label; don't swap them.

`text-muted` is still correct — but only for genuinely de-emphasized **body** text: placeholders, disabled states, timestamps, counts and unit suffixes, inactive glyphs, empty-state copy. Never for a label, an eyebrow, a column header, or any value the user has to read.

### 2.3 Radii

Square corners, everywhere. All four tokens are `0px`:

| Token | Px |
|-------|----|
| `rounded-sm` | 0 |
| `rounded-md` | 0 |
| `rounded-lg` | 0 |
| `rounded-xl` | 0 |

Keep writing against the scale (`rounded-md` on a button, `rounded-lg` on a card) so components stay correct if the scale ever moves; use `rounded-none` where squareness is the *point* and a reader might otherwise assume a radius (chips already do). **Don't use Tailwind's default `rounded-2xl`/`rounded-3xl`** or arbitrary radii (`rounded-[6px]`) — they don't exist in our scale.

**`rounded-full` is reserved for genuine circles only:** status dots, avatars, spinners, radio markers, and **switches** — knob *and* track. The switch is the single sanctioned pill in the system: a square track hugging a round knob read as a mismatch, so `Switch`/`SwitchPill` pair `rounded-full` on both (that is the ruling — it supersedes the earlier square-track rule). Everything else is square — chips, badges, tags, tabs, progress tracks, cards, panels, buttons. A status chip is a **square tag**, never a pill.

**Avatars follow what they depict.** An avatar for a *person* (`Topbar`'s user chip, the members list, the add-member picker) is a genuine circle — `rounded-full`. A monogram tile standing for an *entity* — project, provider, agent — is square (`rounded-md`), like every other entity tile.

### 2.4 Spacing

Use Tailwind's spacing scale (`p-2`, `gap-3`, etc.). Card internal padding defaults: `p-4` (Card md). Tight rows: `py-2`. Section gaps: `gap-4` to `gap-6`. Page container max width via `max-w-6xl` or `max-w-7xl` — pick one per route and stay consistent.

For runtime-computed values only, `var(--space-N)` exists (4/8/12/16/20/24/32/48). Do not use it as a substitute for Tailwind utilities on static layouts.

### 2.5 Shadows / elevation

**Flat tier.** Elevation is expressed as a *rule*, not as a soft shadow — do not invent ad-hoc shadows.

| Token | Value | Use |
|-------|-------|-----|
| `shadow-[var(--shadow-card)]` | `0 0 0 1px var(--border-subtle)` | Default card / pane — a 1px ring, nothing else |
| `shadow-[var(--shadow-float)]` | `0 0 0 1px var(--border-color)` + one neutral drop | **Overlays only** — modal, drawer, dropdown, popover |

Buttons carry no shadow — flat. The rail and masthead carry a `border` instead of any shadow token.

Because `--shadow-card` *is* the 1px ring, don't add a `border` on top of it — pick the ring or the border, not both. A drop shadow is only ever legitimate on a genuinely floating overlay.

### 2.6 Motion

| Token | Duration | Use |
|-------|----------|-----|
| `var(--motion-fast)` | 120ms | Hover color/opacity changes |
| `var(--motion-base)` | 180ms | Default — buttons, cards, focus rings |
| `var(--motion-slow)` | 280ms | Drawer/modal enter, accordion expand |

Easing: `var(--ease-standard)` (`cubic-bezier(0.2, 0, 0.2, 1)`). No bouncy springs. No 500ms+ transitions on UI controls.

Animations defined globally in `index.css` — reuse these rather than writing new keyframes:

- `fade-up` — entrance for new rows.
- `slide-in` — lateral entrance.
- `pulse-dot` — live indicator (`.typing-dots` phase-shifts three of them into a thinking wave).
- `pulse-fade` — the generic loading breathe: a flat opacity pulse. It is what `.skeleton` and `.tracey-thinking-text` run on, and it is the Wire replacement for a gradient shimmer. **Loading states keep their motion but have no gradient sweep** — there is no `shimmer` keyframe, and adding one back is a regression.
- `streaming-border` — a solid accent arc travelling around the 1px rule of a card/row whose contents are mid-stream. It is a `conic-gradient` with **hard stops** (no soft fade), masked to the border — the one place a conic gradient is legitimate.
- `indeterminate-bar` — a sliding sliver for work with no countable progress.
- `tracey-bolt` — a periodic opacity blink on the Ask-Tracey button's bolt icon (the icon dips every ~3s), scoped to `AskTraceyButton`; never on other idle UI. It is not a glow.
- `arrival-flash` — the **live-arrival marker**: a one-shot flat cyan wash that decays over 1.6s on a row that just arrived over the SSE stream (the dashboard live feed and the traces list). It paints as a `::after` overlay, not on the row's `background-color`, so it composes with the row's own hover and selection fills instead of overriding them while it runs. It is a *tint*, not a glow — there is no blur, and "make the new row glow" resolves to this (§9).

Always honor `prefers-reduced-motion` — every animation above already does; new ones must too.

---

## 3. Components — use what exists

`frontend/src/components/ui/` already covers the system. Default to importing, not rebuilding. Inventory:

**Controls:** `Button`, `IconButton`, `Input`, `Textarea`, `Select`, `Checkbox`, `Radio`/`RadioGroup`, `Switch`, `SwitchPill` (labeled switch fused into a single tinted control), `Label`, `FormField`, `SegmentedControl`, `RowButton` (clickable list/grid rows), `Combobox`, `MultiCombobox` (searchable multi-select with optional `maxSelected` cap), `Tabs`, `Menu`, `Tooltip`, `FilterChip`, `FilterDropdown`, `FilterTabs`, `TimeRangePicker`, `Pagination`.

`Select`, `Tabs`, `Tooltip`, `Menu`, `FilterDropdown`, `Combobox`, and `Popover` are **headless Radix** (`@radix-ui/react-*`) styled with our tokens — they handle keyboard nav, focus, and portalling. Never hand-roll a dropdown/menu/tooltip/tab/popover with manual `createPortal` + `getBoundingClientRect` again, and never fall back to a native `<select>` (its option list is OS-rendered and off-theme); reach for these. `Select` keeps the `<option>`-children API but emits the chosen value via `onValueChange(value)`, not a DOM `onChange` event.

**Surfaces:** `Card` (with `Card.Header`/`Body`/`Footer`), `KpiCard`, `EmptyState`, `Skeleton`, `Spinner`, `ListRail` (with `RailHeader` — the locked master/detail left column, see §4).

**Data display:** `DataTable`, `Badge`, `Pill`, `ColoredBadge`, `StatusDot`, `ProgressBar`, `Avatar`, `CodeBlock`, `JsonBlock`, `MessageBubble`, `ToolMessageBubble`, `ModelParametersGrid`, `Collapsible`, `Toast`, `CopyButton`, `CachedTokensHint`, `BrandMark`.

**Overlays:** `Modal`, `Drawer`, `ConfirmDialog`, `DetailPanel`, `StepWizard` (`components/overlays/`), `Popover` (Radix-backed floating panel for rich filter/picker content — for a flat action list use `Menu`).

**Layout:** `Shell`, `Sidebar`, `Topbar`, `NavItem`, `LockedNavItem`, `ProjectSelector` (`components/layout/`).

Shared class recipes live in `components/ui/classes.ts` (`formInputCls`, `fieldLabelCls`, `EYEBROW_CLS`, `kbdCls`, `hoverAccentWashCls`, `hoverRevealOverlayCls`) and `lib/constants.ts` (`FOCUS_RING`, `FOCUS_RING_FIELD`). Import them instead of retyping the strings.

### 3.1 Button rules

`<Button variant="primary|secondary|ghost|danger|dangerOutline|success|link" size="sm|md|lg">`. Defaults: `primary`, `md`. Use:

- **primary** — the one obvious action per screen/section. Save, Run Test, Create Suite. **Flat cyan fill with dark ink** (`bg-accent text-accent-ink`). Hover swaps to `--accent-hover`; active drops to `--accent-press`. No gradient ramp, no bevel highlight, no under-glow. This filled treatment is the primary action's alone; never cyan-fill a label, tab, chip, or input. One per toolbar/region.
- **secondary** — neutral siblings. Cancel, Close, Edit. `bg-card-2` + `border-border`.
- **ghost** — tertiary in toolbars and inline rows.
- **danger** — irreversible, solid red. Always paired with `ConfirmDialog`.
- **dangerOutline** — lower-emphasis destructive (outlined, tints on hover) — e.g. a header-row "Delete".
- **success** — only for "approve" / "promote" semantics; do not use for generic save.
- **link** — inline text action ("Set price →", "View cases ›"): no padding, accent text, underline on hover.

`loading` shows a spinner + disables; `leftIcon`/`rightIcon` for icons; `fullWidth` to fill; `asChild` renders the single child (`<a>` / router `<Link>`) with button styling instead of `<div onClick>`. Write variants (`primary`/`danger`/`dangerOutline`/`success`) auto-emit `data-write` for kiosk gating — pass `data-write` explicitly on a `ghost`/`secondary`/`link`/`IconButton` that mutates.

**Raw `<button>`/`<input>`/`<select>`/`<textarea>` are ESLint-forbidden** (`no-restricted-syntax`) everywhere except the `components/ui/` primitive layer — use the primitive. Icon-only → `IconButton`; clickable list/grid row → `RowButton`. For a genuinely bespoke control (range slider, labeled switch-pill, command palette) add a one-line `// eslint-disable-next-line no-restricted-syntax -- <reason>`.

### 3.2 Card rules

`<Card elevation="flat|raised|floating" padding="none|sm|md|lg">`. Defaults: `raised`, `md`.

- **flat** — list rows, inline panels inside another card (`bg-card` + `border-hairline`).
- **raised** — default standalone card (`bg-card` + the 1px `--shadow-card` ring).
- **floating** — only inside overlays (`bg-surface-2` + `border-border` + `--shadow-float`).

Use `Card.Header` / `Card.Body` / `Card.Footer` for structure. `accentBar` paints a 3px entity-colored bar along the top edge (e.g. an agent card colored by `agentColor`). `hoverGlow` is a legacy prop name for a **colored 1px hover ring**, not a halo — it swaps the ring color, nothing blurs. Never put a drop shadow on a card.

### 3.3 Tag / Badge rules

Status, kind, and entity tags = `Pill` or `Badge` (both render `Badge`; `Pill` and `ColoredBadge` are thin `variant="tinted"` wrappers). They are **square tags**: `Badge` hard-codes `rounded-none` and has **no `shape` prop** — the prop and the `BadgeShape` type were removed, and `ColoredBadge`'s pass-through went with them. Don't add a rounded variant back.

Always pair with semantic color when meaning is fixed (success / warn / danger) and with `modelColor` / `agentColor` when meaning is per-entity. Tags are read at a glance — keep the label ≤ 18 chars, no full sentences. The optional `dot` renders a 5px circle (`rounded-full` — a genuine circle, allowed).

### 3.4 DataTable rules

Use `DataTable` for any tabular dataset > 5 rows; its header row already carries `EYEBROW_CLS` + `border-b border-hairline`, so column labels come out mono/uppercase/tracked for free. For trace lists specifically, the existing `trace-row` pattern (CSS grid, `border-bottom` on `--border-subtle`, hover wash) is the canonical layout — follow it for any new high-volume scrolling list.

### 3.5 Empty + loading states

- **Empty:** `EmptyState` with a one-line headline + one-line hint + optional CTA. No clipart.
- **Loading:** `Skeleton` for shaped placeholders (always reserve the final layout's height to prevent jump) — it is a flat `bg-card-2` block that breathes via `pulse-fade`, **not** a gradient sweep. `SkeletonList` for a rows placeholder. `Spinner` only for inline button loading and indeterminate small areas. `streaming-border` class on a card whose contents are mid-stream.
- **Error:** Inline `text-danger` message near the failing control. For full-page failures, `EmptyState` variant with the danger color.

### 3.6 Form controls, toggles, and menus

- **Text/number/password** → `Input` (`leftAddon`/`rightAddon` for icons/affordances); **long text** → `Textarea`; **short option list** → `Select` (Radix-backed styled dropdown, `<option>` children + `onValueChange`); **searchable/entity list** → `Combobox`; **searchable multi-select** (pick several from a long list, optional cap) → `MultiCombobox`. Wrap each in `FormField` (or pair with `Label`). Inline (flex-row) fields need a width wrapper — `Input`/`Select` are `w-full`.
- **Boolean** → `Switch` (on/off) or `Checkbox`; **one-of-N** → `Radio`/`RadioGroup`, or `SegmentedControl` for a compact toggle bar. On a `Switch`/`SwitchPill` both the **track and the knob are round** (`rounded-full`) — the switch is the system's one sanctioned pill silhouette (§2.3). Don't square either half.
- **Tabs** → `Tabs` (pass `data-testid` per item where e2e needs it). **Dropdown menu** → `Menu` + `Menu.Item`/`Menu.Separator`. **Tooltip** → `Tooltip` (the single `TooltipProvider` is already mounted in `App.tsx`).

---

## 4. Layout patterns

- **App shell:** `Shell` provides the nav rail + masthead + main scroll area. Don't render a custom shell per route. The three panes are **flush and full-bleed** — the only things between them are 1px rules:
  - **Topbar (masthead):** flat and full-width — `h-[48px]`, `bg-surface-2`, `border-b border-border`. No margin, no radius, no shadow, no blur. Its breadcrumb is **mono** (`font-mono text-title`): project name, `/`, page label.
  - **Sidebar (rail):** flat — `bg-sidebar`, `border-r border-border`, no margin/radius/shadow. `md:w-[216px]` expanded, `md:w-16` collapsed; below `md` it becomes an off-canvas drawer.
  - **Main:** `Shell`'s `<main>` uses `p-[10px]` **padding**, not a gutter margin, so the scroll container stays flush to every edge (that also keeps overlay scrollbars off the content on Firefox/Linux). `Shell` and `<main>` are `bg-transparent`.
- **Nav items carry a mono two-letter page code, not an icon.** `NavItem` / `LockedNavItem` render `code` in an 18px-wide `font-mono text-caption` slot where a glyph used to sit — `text-accent` when active, `text-muted` otherwise. The `icon` prop and the whole `NavEntry.icon` / `NavIconName` / `NAV_ICONS` mechanism are gone. Codes (`TR`, `AG`, `DB`, `EV`, …) are declared once as the `NavCode` string-literal union in `components/layout/shellNav.tsx`; that union is also what keeps them exempt from the Lingui `no-unlocalized-strings` rule — **they are technical glyphs, not copy, so never wrap a page code in `<Trans>`**. The active row also gets a 2px `bg-accent` bar at its left edge plus the `nav-active` wash (`--bg-wash-active`).
- **Page header:** `text-h1` title, `text-body-sm text-muted` subtitle one line below, primary action top-right.
- **Section header:** use sentence case `text-h2 font-semibold` for a *title*. Use `EYEBROW_CLS` (mono/uppercase/tracked) for a *label* that names a column, a metric, or a strip — that is the Wire system's uppercase treatment, and it is the only one.
- **KPI rows:** `KpiCard` grid, 3–5 cards, `text-display` for the value, `EYEBROW_CLS` for the label, optional delta with semantic color.
- **Detail views:** Drawer (right-side) for entity detail when the list still matters (traces, runs, proposals). Modal for focused tasks (create wizard, confirm). Full-page route only when the detail has its own sub-navigation.
- **Master/detail list rail — the locked left column.** Every master/detail view (Agents, Evaluators, Test Suites, Test Runs, Evaluator Playground) shares one left-column shell: `ListRail` (`components/ui/ListRail.tsx`). **Do not hand-build a list header again.** Its anatomy is fixed, top to bottom:
  1. **Header** (`RailHeader`): a sentence-case title + optional count (rendered with `EYEBROW_CLS`) on Row A; an optional primary **create** button (Row B); an optional **search** box (Row C). Omitted slots collapse — a view with no create (Agents, Runs) or no search (Runs) still reads as the same panel, so don't reserve phantom gaps.
  2. **Filter band:** one locked slot below the header for the view's filter control (`SegmentedControl`, `FilterDropdown size="sm"`, or a bespoke toggle). Keep its contents `w-full` / `size="sm"` so heights stay consistent across views.
  3. **Body:** the scrolling rows, with built-in loading (`SkeletonList`) and empty (`EmptyState`) states. The view passes rows as `children` and owns their inner flex/gap; pass `isEmpty` so the shell swaps in the empty node.

  The shell is a framed pane (`RAIL_CARD_CLS` = `bg-card` + the 1px `--shadow-card` ring), so rows sit flat on it — inactive rows are `bg-transparent hover:bg-card-2` (**no per-row shadow**; the shell owns the ring). Lock the split width with the exported `LIST_RAIL_COLS` grid template — never hand-tune per-view column widths. **Exception:** the Evaluator Playground rail is two stacked pickers (evaluator + past evaluations), so it reuses the pane shell, `RailHeader`, and selection treatment but keeps its own two-section body instead of `ListRail`'s single list. *(A future `MasterDetail` wrapper could also own the right pane + mobile select-first behavior; today only the left column is shared.)*
- **Selection treatment — canonical, three parts, nothing else.** A selected row is:
  1. a **flat entity-colored tint** — `color-mix(in srgb, <color> 13%, var(--bg-card))`;
  2. a **1px inset ring** at 45% of the same color — `inset 0 0 0 1px color-mix(in srgb, <color> 45%, transparent)`;
  3. a **3px leading bar** in the solid color, absolutely positioned at the row's left edge.

  **No gradient, no glow, no drop shadow.** Use `lib/selectionRow.ts` (`selectionRowStyle` / `selectionBarStyle` / `SELECTION_ROW_INACTIVE`) for runtime-hex colors (agent/evaluator/model/provider/detector), or the matching `categorySelectedRow` classes in `features/evaluators/categoryClasses.ts` for token colors. Don't layer an extra wash or ring class on top.
- **Responsiveness:** the app must stay usable down to 1024px wide. The sidebar starts collapsed below 1280px (`Shell` checks `matchMedia` once at mount; the user's toggle is never overridden). Inside a master/detail pane, **don't use viewport breakpoints (`lg:`/`xl:`) to split columns — they lie about the pane's actual width** (rail + list eat into it). Use Tailwind 4 container queries instead: `@container` on the pane root, `@3xl:grid-cols-[…]` on the split (see `AgentDetail.tsx`). Multi-column stat/KPI strips wrap (`flex-wrap` with a content-true `min-w`, or `grid-cols-[repeat(auto-fit,minmax(…,1fr))]`) rather than overflow. Shared row grids (e.g. `COL_WIDTHS` in `tracesMeta.ts`) use `minmax(min,max)` columns so they compress before clipping. On a **fixed** track (the dashboard live feed), a cell must clamp itself — `max-w-full min-w-0 overflow-hidden`, plus `justify-self-center` where it's centered — or content wider than the track overflows across the neighbouring columns instead of scrolling. `Badge`/`Pill` is built for this: it truncates its label with an ellipsis once the cell gives it a width, and `Pill` keeps the full value in `title`.
- **Mobile (< `md`, 768px) — monitoring tier:** phones get a read/monitor experience; authoring stays desktop-first. The patterns:
  - **Shell:** the sidebar becomes an off-canvas drawer (backdrop + slide-in, closes on nav click); the topbar drops the search box and license badge below `sm` and the health indicator collapses to its dot below `lg`. Behavior branches use `useIsMobile()` (`hooks/useMediaQuery.ts`); styling branches use `max-md:`/`md:` classes.
  - **Master/detail pages** (agents, runs): list and detail become separate screens. Only an *explicit* `?id=` selection opens the detail (the desktop select-first default is suppressed on mobile) and a ghost "All …" back button clears it — see `Runs.tsx` / `Agents.tsx`.
  - **Wide row grids** (trace list): low-priority columns collapse via container query — the list declares `@container` + exposes full/narrow templates as CSS vars, rows share `TRACE_GRID_CLS`, hidden cells carry `@max-2xl:hidden` (`tracesMeta.ts`).
  - **Full-height internal-scroll layouts** become natural page scroll below `md` (`md:h-full md:min-h-0` on the page root — see `Traces.tsx`); horizontal stat strips that can't wrap get `overflow-x-auto`.
    - **Exception — virtualized lists stay bounded scrollers at every breakpoint.** `TraceTable` carries `max-md:flex-none max-md:h-[60svh]`, so below `md` it is a fixed-height internal scroller inside the page scroll rather than expanding to its content. This is not a style preference: a virtualizer measures its scroll container, so an unbounded one reports every row as visible, which permanently satisfies the infinite-scroll load-more trigger and walks the entire result set. Any future virtualized list needs the same treatment.

---

## 5. Icons

- **Source:** the in-repo icon set at `components/icons` (import through its barrel; the `Svg` wrapper handles `size`/`currentColor`). `lucide-react` is **not** a dependency — don't add it. See BEST_PRACTICES.md §6 for the code rule. Default size 16. 14 in dense rows, 20 in headers, 24 only in empty states.
- **No emoji as icons. Ever.** Even in placeholder copy.
- **Stroke width:** default (2). Don't mix stroke widths within a screen.
- Always pair an icon-only button with `aria-label`.
- In the nav rail, icons are *replaced* by mono page codes (§4) — don't reintroduce a glyph column there.

---

## 6. Inline styles policy

The default is **no inline styles**. Use Tailwind utilities, including arbitrary-value syntax for tokens:

```tsx
// good
<div className="bg-card rounded-lg shadow-[var(--shadow-card)] p-4">
<button className="bg-accent hover:bg-accent-hover">

// bad — static value as inline style
<div style={{ background: '#101a23', borderRadius: 0 }}>
```

Inline `style={{ ... }}` is acceptable **only** for genuinely runtime-computed values: a percent width from data, a runtime hex from `modelColor()` / `agentColor()`, the selection tint from `selectionRowStyle()`, a numeric prop forwarded to a primitive. If the value is static — including a static `color-mix` against a token — it belongs in className.

---

## 7. Interaction & accessibility (non-negotiable)

- **Cursor:** `cursor-pointer` on every clickable surface, including cards. Already encoded in `Button` and in `Card` when `interactive` or `hoverGlow`.
- **Hover:** color/background change only. Never a `scale()` transform on elements that share layout flow — it shifts neighbors. A bg wash (`--bg-wash-hover`, `hoverAccentWashCls`) or a ring-color change is the pattern; a blurred halo is not.
- **Focus:** every interactive element gets a visible focus ring. The canonical string is `FOCUS_RING` in `lib/constants.ts` — `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]`. `Button` already applies it — import it in custom controls instead of retyping it.
  - **Composite text fields ring their frame, not their input.** When the wrapper draws the frame and the input inside it is deliberately borderless (the Tracey composer; `Input`'s addon branch frames the same way with its own recipe, below), the ring goes on the wrapper — a second rectangle floating inside the frame reads as a rendering bug. Use `FOCUS_RING_FIELD` (`lib/constants.ts`): the same 2px/60% ring, scoped with `has-[textarea:focus]:` rather than bare `focus-within:`, because `focus-within:` also fires for focusable *siblings* inside the frame (a Send button) — and a lit frame must keep meaning "typing lands here", not "focus is somewhere in this box". A wrapper ring needs `transition-[border-color,box-shadow]`: a ring is a box-shadow, and `transition-colors` does not animate it.
  - The input family's own treatment (`formInputCls`, `Input`'s addon branch — `border-accent` + `ring-1` at 45%) is **not** a second canonical ring; the contrast there is carried by the solid border, with the 1px ring as a halo around it. Don't copy the 45% ring onto a surface that has no `border-accent` to sit against, and don't "harmonize" a `FOCUS_RING` control down to it.
- **Keyboard:** Tab order = visual order. Modal/drawer trap focus and close on Esc (the existing components do; new ones must).
- **Labels:** every form input pairs with a `<label>` (use `FormField`). Icon-only buttons get `aria-label`. Decorative glyphs (nav page codes, status dots) are `aria-hidden` and must have a real text label or `title` beside them.
- **Contrast:** on `bg-card`, `text-primary` measures ~13:1, `text-secondary` ~6.5:1, `text-muted` ~3.6:1. So: `text-primary` anywhere; `text-secondary` for any label or prose, **including every eyebrow**; `text-muted` for captions/placeholders/inactive glyphs only — never for body content, never for an eyebrow, never for a value the user has to read. Pair a `*-subtle` background only with its matching solid text color.
- **Status by color is never the only signal.** Pair with an icon, label, or shape (a green dot beside the word "Passed", not a green dot alone).
- **Reduced motion:** if you add a keyframe animation, also add a `@media (prefers-reduced-motion: reduce)` rule that disables it.

---

## 8. Real-time / streaming UI

- Live trace incoming → `streaming-border` class on its row/card while open; `pulse-dot` on the live indicator in the top bar.
- New rows entering a list animate with `fade-up` (already in CSS), and a row arriving over a **live stream** also carries `arrival-flash` — the one-shot cyan wash that marks it as new (§2.6). Don't reorder existing rows on insert — only prepend or append per the list's stated sort.
  - **In a virtualized list, `fade-up` is wrong**: the virtualizer positions each row with its own `transform`, and `fade-up` animates `transform`, so the row would animate from the container's origin instead of its slot. `arrival-flash` touches only the overlay, which is why it is the arrival treatment there (`TraceTable`).
- Never block interaction during background updates. SSE updates are partial — patch the cached query, don't refetch the page. **A list must never blink through its loading state on a live arrival**: an empty list renders its skeleton, so anything that empties the cache (`resetQueries` on an infinite query) redraws the whole table on every event. Patch the cached pages instead (`features/traces/traceHeadMerge.ts`).

**There is no showpiece exception.** Earlier revisions of this guide granted the Dashboard and
Tracey a scoped licence to exceed the flat-instrument baseline. **That licence was revoked and the
code was flattened to match** — the radial/aurora washes, the gradient-clipped display type, the SVG
glow filters, and the gradient shimmer are all gone from the codebase. Every route obeys §1–§7. The
two subsections below survive only to record what is *distinctive* about those routes (motion and
type tokens) — not what is exempt from the rules.

## 8.1 Dashboard tier — motion, not atmosphere

The dashboard (`features/dashboard/`, route `/dashboard`) is the product's stakeholder-facing
surface. What is scoped to it is **motion and density**, nothing atmospheric:

- **Dashboard keyframes** (`index.css`, "Dashboard showpiece tier" block): `pulse-sweep` (a solid
  2px playhead crossing the pulse band on arrival — a rule, not a beam), `pulse-idle-sweep` (a slow
  7%-tint segment across an idle band, so a flatline reads as intentional), `chart-draw-in` (a
  stroke-dashoffset line draw), and `digit-tick` (a one-frame odometer nudge when a live counter
  changes). Dashboard-only — do not use them on other routes. (`arrival-flash` started here and is
  now shared: the traces list marks live arrivals with it too, so it lives in the common motion set
  in §2.6.)
- **Display-tier type**: the hero token number is `text-[68px]` and the pulse counters are mono
  `text-display`. The 68px hero figure is the single sanctioned size outside the type scale;
  don't read it as permission to invent others.

Everything else binds exactly as elsewhere: **no glow, no gradient, no atmosphere.** The SVG glow
filters that once sat on the pulse line and the gauge, and the `drop-shadow` on the error counter,
were deleted — there is no "glow as a data signal" carve-out any more. Charts use flat
semi-transparent fills (§8.3). `prefers-reduced-motion` guards every keyframe above.

## 8.2 Tracey tier — reading surface

Tracey AI (`features/tracey/`, route `/tracey-ai`) is a prose-reading surface rather than a data
grid, which earns it two type tokens and one identity treatment — and nothing else:

- **Reading-tier type**: message text, the composer, and the user bubble sit at `text-chat` (15px)
  with in-chat markdown headings at `text-chat-title` (16px) / `text-h1` — see `chat-markdown.tsx`.
  The empty-thread hero line uses `text-display`. `text-chat` / `text-chat-title` are
  **Tracey-only**; do not use them on data-dense views.
- **Tracey tier CSS** (`index.css`, "Tracey assistant tier" block), all flat:
  - `tracey-halo` / `tracey-halo-active` — a **static 1px accent rule** around Tracey's avatar that
    brightens to `--accent-hover` while a turn runs. It does not rotate, spin, or blur; the class
    name is historical.
  - `tracey-gradient-text` — despite the name, plain `color: var(--accent-hover)` on the "Tracey AI"
    wordmark and the empty-thread hero line. There is no gradient clip. Don't add one back.
  - `tracey-thinking-text` — the "Thinking…" label: `text-secondary` running the shared `pulse-fade`
    opacity pulse. Not a shimmer wash.
  - There is **no** `tracey-aurora`. The drifting background glow was deleted along with every other
    background wash (§2.1); the chat panel sits flat on the surface like any other pane.
- **User bubble finish**: the flat cyan fill with `text-accent-ink` (normally the primary
  button's alone) also dresses the user's chat bubble.

Everything else binds: token palette only, square corners, the flat shadow tier,
`prefers-reduced-motion` guards (the thinking pulse degrades to plain secondary text; the halo is
static either way), accessibility rules (§7), and no glassmorphism.

## 8.3 Sanctioned effects — the complete list

Flat is the rule; this is the whole set of exceptions, and it is closed. Anything not on this list
is an anti-pattern (§9).

- **`shadow-[var(--shadow-float)]` on a genuinely floating overlay** — modal, drawer, dropdown,
  popover. The only legitimate drop shadow in the product. Everything else uses `--shadow-card`,
  which is a 1px ring.
- **`backdrop-filter: blur(4px)` on the modal overlay** (`.modal-overlay`) — the one sanctioned
  blur. Not on panels, not on the topbar, not "for depth".
- **Flat semi-transparent chart area fills** — `fill={color} fillOpacity={…}` on the area path
  (`MiniArea` defaults to `0.18`; callers may tune it within roughly 0.14–0.22 — `StatTile` does)
  (`AreaChart`, `DensityCurve`, `MiniArea`). A constant-alpha fill, not a fade-to-transparent ramp:
  the `<defs>` / `<linearGradient>` blocks were deleted. Don't reintroduce a gradient to "soften"
  a chart.
- **Motion without gradient** — the keyframes listed in §2.6 and §8.1. Skeletons and thinking
  labels keep their *motion* (`pulse-fade`) but have no gradient sweep; `streaming-border` is a
  conic gradient with hard stops, masked to a 1px rule, and is the sole conic in the system.
- **The 3px accent bars** — `Card accentBar`, the selection leading bar (§4), the active nav bar.
  Solid entity/accent color, no blur.

---

## 9. Anti-patterns — do not introduce these

- **Pill-shaped chips.** `rounded-full` on anything that is not a genuine circle or a switch — chips, badges, tags, tabs, progress tracks, buttons. Circles are dots, avatars and spinners; switches are the one sanctioned pill; everything else is a square tag (§2.3).
- **Dimensional gradients on fills.** A fill is one flat color (or one flat alpha — see the chart fills in §8.3). No top-to-bottom lightening ramp, no fade-to-transparent, no bevel highlight, no inner sheen, no under-glow, no "raised" edge on buttons, chips, cards, tracks, or chart areas.
- **Gradient-clipped text.** No `background-clip: text` wordmarks, hero numbers, or headings. They were all removed; identity text is a solid token color (§8.2).
- **Background atmosphere.** Aurora washes, film grain, page-level radial glows, per-page background effects. They were deleted from `body` **and from every route including the dashboard and Tracey**; there is no exception left. Do not reintroduce them.
- **Floating-panel shell.** Margins, gutters, radii, or shadows on the rail, the masthead, or the main pane. Panes are flush and divided by 1px rules.
- **Glow as an affordance — or as data.** A colored blur to signal hover, selection, liveness, or a metric. Selection is tint + inset ring + bar (§4); hover is a wash or a ring-color change; a live signal is a dot, a rule, or motion. The dashboard's SVG glow filters and `drop-shadow` counters were deleted — the old "glow as a data signal" carve-out is revoked.
- **Gradient shimmer on loading state.** Skeletons breathe with `pulse-fade`; they do not sweep a highlight across themselves. Keep the motion, drop the gradient.
- **Gold / amber as accent or chrome.** The accent is cyan; amber exists only as `--warn`.
- **`text-white` on a colored fill.** Use the matching `-ink` token (§2.1).
- Glassmorphism, frosted blur on regular surfaces — the modal overlay's `backdrop-filter: blur(4px)` is the one sanctioned use (§8.3), with no per-route exception.
- Vibrant rainbow gradients, neon glows, or animated gradients on idle UI — no scoped exceptions; the dashboard and Tracey obey this like every other route.
- **Keeping dead API as a no-op "for compat".** When a prop, token, class, or variant is removed, remove it outright — no accepted-and-ignored props, no aliases that resolve to nothing. `Badge`'s `shape` prop and the nav `icon` mechanism were deleted, not stubbed.
- Light-mode styles. Proxytrace is dark-only; do not add light-mode classes "just in case." If/when light mode happens, it'll be a tracked initiative with new tokens.
- Custom shadows / radii / type sizes outside the scale — including `rounded-2xl`, `rounded-3xl`, and arbitrary `rounded-[Npx]`.
- `border-2` or thicker borders on UI chrome — our borders are 1px rules. Thicker only on focus rings (2px) and explicit dividers.
- Decorative emoji in copy, headings, or buttons.
- Scale transforms on hover for elements in flow layout.
- `<div>` with onClick instead of a real button or anchor.
- Wrapping a nav page code (`TR`, `AG`, …) in `<Trans>` — they are technical glyphs, not copy (§4).
- `useEffect` to run a one-time fetch — use TanStack Query.
- Fetching inside a component when an SSE broadcaster already streams the data.
- `dangerouslySetInnerHTML` to render captured prompts / model output — render as text (`CodeBlock`, `<pre>`). See BEST_PRACTICES.md §12.
- Unsanitized data-derived `href`/`src` (allow only `http`/`https`/`mailto`); `target="_blank"` without `rel="noopener noreferrer"`.

---

## 10. Pre-flight checklist

Before opening a frontend PR, verify:

- [ ] No new hex values, no new px sizes outside the scale, no ad-hoc shadows.
- [ ] All static styles are Tailwind utilities; inline `style` only for runtime-computed values. Complex statics use arbitrary-value syntax (`shadow-[var(--shadow-card)]`), never inline `style`.
- [ ] Tokens used via `bg-card` / `text-primary` / `rounded-lg` / `shadow-[var(--shadow-…)]`, not raw hex or px.
- [ ] Corners are square: no `rounded-full` except on a genuine circle (dot, person avatar, spinner) or a switch (knob and track); no `rounded-2xl`/`rounded-3xl`/`rounded-[Npx]`.
- [ ] Fills are flat: no gradient ramp, bevel, sheen, or glow on a button, chip, card, track, or chart area — and no gradient-clipped text. No background wash on any route (§8).
- [ ] Text on a colored fill uses the matching `-ink` token (`text-accent-ink` / `text-danger-ink` / `text-success-ink`), never `text-white`.
- [ ] Reused existing component (`Button`, `Card`, `Badge`, `Modal`, `Drawer`, `ListRail`, etc.) — no duplicate primitives.
- [ ] Small uppercase labels use `EYEBROW_CLS`, not a hand-rolled `text-caption uppercase` string, and land on `text-secondary`.
- [ ] Cursor, hover, focus-visible (`FOCUS_RING`, or `FOCUS_RING_FIELD` on a composite text field's frame) all present on interactive elements.
- [ ] Icon-only buttons have `aria-label`.
- [ ] Form inputs use `FormField` (label + control).
- [ ] Empty / loading / error states exist for any async-driven view.
- [ ] No emoji-as-icon; every icon comes from `components/icons`.
- [ ] No `prefers-reduced-motion`-blind animations.
- [ ] No light-mode classes.
- [ ] Untrusted content (prompts, model output, tool args) rendered as text — no `dangerouslySetInnerHTML`. Data-derived URLs scheme-checked; external links have `rel="noopener noreferrer"`. (Full rules: BEST_PRACTICES.md §12.)
- [ ] Streaming/live state uses `streaming-border` / `pulse-dot` where applicable.
- [ ] List/table layout matches `trace-row` / `DataTable` conventions; master/detail rails use `ListRail` + the canonical selection treatment.
- [ ] Visually scanned at 1024px, 1280px and 1440px widths; nothing horizontally scrolls. Master/detail panes use container queries (`@container` + `@3xl:` etc.), not viewport breakpoints (§4).

---

## 11. When to extend this guide

If you're about to introduce a *new* primitive, *new* token, or *new* pattern that future code will follow — pause, propose it, and update this file in the same PR. New tokens go in `index.css` and the `@theme` block; new primitives go in `components/ui/`; new shared class recipes go in `components/ui/classes.ts`. Drift in unowned files is the enemy.
