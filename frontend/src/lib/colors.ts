import { EvaluatorKind } from '../api/models';

const MODEL_COLORS: Record<string, string> = {
  'gpt-4o': '#57c4d3',
  'gpt-4o-mini': '#d9a23f',
  'gpt-5.4': '#57c4d3',
  'gpt-5.4-mini': '#d9a23f',
  'gpt-4.1': '#57c4d3',
  'gpt-3.5-turbo': '#57c4d3',
  'claude-3.5-sonnet': '#5aba80',
  'claude-sonnet-4-5': '#5aba80',
  'claude-3-opus': '#5aba80',
  'claude-3-haiku': '#5aba80',
};

// Fallback palette for models with no explicit MODEL_COLORS entry. Models are intentionally
// grouped by family (all gpt cyan, all mini amber, all claude green), so this stays inside the
// cyan/amber/green Wire anchors and may repeat — distinctness between families is not the goal here.
const MODEL_PALETTE = ['#57c4d3', '#d9a23f', '#5aba80', '#7d95c9', '#7dd3e0', '#d9a23f', '#5aba80', '#57c4d3'];

// Per-entity categorical palette — hash-assigned by agentColor/projectColor/providerColor to give
// each agent/project/provider a stable, at-a-glance-distinct color in charts, legends, and badge
// dots. Unlike the semantic tokens (which deliberately group: success/warn/danger), these must be
// MUTUALLY DISTINCT, so they walk the hue wheel. They are derived from the Wire anchors (accent
// cyan #57c4d3, warn amber #d9a23f, success green #5aba80) by holding a muted saturation/lightness
// band and rotating hue to fill the gaps the ~5 semantic tokens can't cover — the set still reads
// as one calm family (no neon), every entry clears AA (≥4.5:1) as text on the ink surfaces, and
// no two collide. Data-encoding only; never use these for chrome/CTAs/semantics (DESIGN.md §2.1).
const AGENT_PALETTE = [
  '#57c4d3', // cyan   — brand accent
  '#d9a23f', // amber
  '#5aba80', // green  — brand success
  '#8a92d8', // periwinkle
  '#c886b8', // orchid
  '#9cc46a', // lime
  '#d3737f', // rose
  '#ae8dc9', // violet
];

const PROVIDER_COLORS: Record<string, string> = {
  Anthropic: '#5aba80',
  OpenAI: '#57c4d3',
  Google: '#d9a23f',
  Azure: '#7d95c9',
  Mistral: '#d3737f',
};

function hashStr(s: string): number {
  let h = 0;
  for (let i = 0; i < s.length; i++) {
    h = (Math.imul(31, h) + s.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}

export function agentColor(id: string): string {
  return AGENT_PALETTE[hashStr(id) % AGENT_PALETTE.length];
}

export function modelColor(name: string): string {
  return MODEL_COLORS[name] ?? MODEL_PALETTE[hashStr(name) % MODEL_PALETTE.length];
}

export function providerColor(name: string): string {
  return PROVIDER_COLORS[name] ?? AGENT_PALETTE[hashStr(name) % AGENT_PALETTE.length];
}

export function projectColor(id: string): string {
  return AGENT_PALETTE[hashStr(id) % AGENT_PALETTE.length];
}

/** Stable per-scope color (scope tags on trace rows, scope rail selection). */
export function scopeColor(id: string): string {
  return AGENT_PALETTE[hashStr(id) % AGENT_PALETTE.length];
}

/** Stable per-detector color for custom anomaly detectors (rail selection, accents). */
export function detectorColor(id: string): string {
  return AGENT_PALETTE[hashStr(id) % AGENT_PALETTE.length];
}

export const EVALUATOR_KIND_COLOR: Record<EvaluatorKind, string> = {
  [EvaluatorKind.Agentic]: '#57c4d3',
  [EvaluatorKind.ExactMatch]: '#7d95c9',
  [EvaluatorKind.NumericMatch]: '#7d95c9',
  [EvaluatorKind.JsonSchemaMatch]: '#7d95c9',
};

/**
 * Color for an evaluator from its kind string (the API serializes the kind as its name).
 * Falls back to steel-blue for any kind not in the palette so a border is never left uncolored.
 */
export function evaluatorColor(kind: string): string {
  return EVALUATOR_KIND_COLOR[kind as EvaluatorKind] ?? '#7d95c9';
}

export const EVALUATOR_KIND_CATEGORY: Record<EvaluatorKind, 'llm' | 'rule'> = {
  [EvaluatorKind.Agentic]: 'llm',
  [EvaluatorKind.ExactMatch]: 'rule',
  [EvaluatorKind.NumericMatch]: 'rule',
  [EvaluatorKind.JsonSchemaMatch]: 'rule',
};

/**
 * Semantic color for an HTTP status, returned as a token reference (not a literal) so the
 * value tracks `--success`/`--warn`/`--danger` instead of drifting from them. The result is only
 * ever consumed as a CSS value (inline `color`/`background`), where `var()` resolves normally.
 */
export function statusColor(httpStatus: number): string {
  if (httpStatus >= 200 && httpStatus < 300) return 'var(--success)';
  if (httpStatus >= 400 && httpStatus < 500) return 'var(--warn)';
  return 'var(--danger)';
}

/**
 * Mixes a runtime color toward transparent — `pct` is the opacity of the color.
 * For static token bases (constant color + constant pct) prefer a Tailwind
 * arbitrary class (`bg-[color-mix(...)]`); use this only for data-driven colors.
 */
export const tint = (color: string, pct: number): string =>
  `color-mix(in srgb, ${color} ${pct}%, transparent)`;
