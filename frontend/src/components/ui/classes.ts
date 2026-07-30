import { cn } from '../../lib/cn';

/*
 * Focused-field recipes (DESIGN §7). One paint — a solid 1px accent rule plus the canonical
 * 2px accent ring at 60% — in the two scopes a class string cannot abstract over: the control
 * itself, and a wrapper that frames a borderless control.
 *
 * The 60%/2px numbers are not taste. `color-mix(… accent X%, transparent)` is premultiplied, so
 * the ring composites straight onto the surface behind it: at 45% it measures 2.70–2.77:1 on our
 * four surfaces and at 40% only 2.41–2.44:1, both under WCAG 2.2's 3:1 floor for a focus
 * indicator; 60% measures 3.73–4.01:1. And 1px of ring beside the 1px rule leaves the indicator
 * area a hair under a 2px perimeter, so the ring is 2px. Pair with
 * `transition-[border-color,box-shadow]` — a ring is a box-shadow, and `transition-colors`
 * does not animate it.
 */

/** Focus treatment for a control that *is* the field — a real `input` / `textarea` / trigger. */
export const fieldFocusCls = cn(
  'focus:border-accent focus:ring-2 focus:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]',
);

/** The same, on a wrapper that draws the frame around a borderless control (`Input`'s addons). */
export const fieldFocusWithinCls = cn(
  'focus-within:border-accent focus-within:ring-2 focus-within:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]',
);

/**
 * Invalid field. The danger border is already solid at rest, so the ring is the only pixel that
 * changes on focus and has to carry the indicator alone — hence solid `--danger` rather than a
 * mix: `#dd5959` needs ~76% alpha before it clears 3:1 on `bg-card-2` (45% measures 1.82:1).
 */
export const fieldInvalidCls = cn(
  'data-[invalid=true]:border-danger data-[invalid=true]:focus:border-danger',
  'data-[invalid=true]:focus:ring-danger',
);

export const formInputCls = cn(
  'w-full px-3 py-2 bg-card-2 border border-border rounded-md',
  'text-title text-primary font-[inherit] outline-none',
  'transition-[border-color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-standard)]',
  fieldFocusCls,
  'disabled:opacity-50 disabled:cursor-not-allowed',
  fieldInvalidCls,
);

/**
 * A field-shaped box that shows a value the user cannot change (a locked scope, a pre-selected
 * suite). Deliberately **not** a `disabled` control: `formInputCls` dims those to 50% opacity, and
 * a value the user still has to read must not be dimmed (DESIGN §7, contrast). Pair it with a line
 * of copy saying *why* it is fixed.
 */
export const readonlyFieldCls = cn(
  'w-full px-3 py-2 bg-card-2 border border-border rounded-md text-title text-primary truncate',
);

/**
 * Top-right overlay control revealed on hover/focus of a `group` parent.
 * Used for the per-message copy button in trace detail blocks.
 */
export const hoverRevealOverlayCls = cn(
  'absolute top-1.5 right-2 z-10',
  'opacity-0 transition-opacity duration-[var(--motion-fast)] ease-[var(--ease-standard)]',
  'group-hover:opacity-100 focus-visible:opacity-100',
);

/** Canonical form-field label (uppercase eyebrow). Shared by `Label` + `FormField`. */
export const fieldLabelCls = 'text-caption font-semibold text-secondary uppercase tracking-[0.05em]';

/** Signal Desk eyebrow: mono, uppercase, tracked — table headers, KPI labels, rail counts. */
export const EYEBROW_CLS = cn('font-mono text-caption uppercase tracking-[0.14em] text-secondary');

/** Faint accent wash on hover for interactive rows/cards (trace rows, fleet rows, list rows). */
export const hoverAccentWashCls = 'hover:bg-[color-mix(in_srgb,var(--accent-primary)_4%,transparent)]';

/** Canonical `<kbd>` key cap for keyboard hints (slash menu, palettes, shortcut legends). */
export const kbdCls = cn(
  'rounded-sm border border-hairline bg-card px-1 py-px font-mono text-caption text-secondary',
);