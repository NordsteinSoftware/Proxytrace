import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import { ProposalKind } from '../../api/models';

export type DisplayTone = 'accent' | 'success' | 'danger' | 'muted' | 'neutral' | 'secondary' | 'teal';

/** Tone → text-color Tailwind class (arbitrary value over the tone's CSS var). */
export const TONE_TEXT: Record<DisplayTone, string> = {
  accent: 'text-[var(--accent-primary)]',
  success: 'text-[var(--success)]',
  danger: 'text-[var(--danger)]',
  muted: 'text-[var(--text-muted)]',
  neutral: 'text-[var(--text-muted)]',
  secondary: 'text-[var(--text-secondary)]',
  teal: 'text-[var(--teal)]',
};

/** Tone → solid background Tailwind class (e.g. for status dots). */
export const TONE_BG: Record<DisplayTone, string> = {
  accent: 'bg-[var(--accent-primary)]',
  success: 'bg-[var(--success)]',
  danger: 'bg-[var(--danger)]',
  muted: 'bg-[var(--text-muted)]',
  neutral: 'bg-[color-mix(in_srgb,var(--text-muted)_60%,transparent)]',
  secondary: 'bg-[var(--text-secondary)]',
  teal: 'bg-[var(--teal)]',
};

/** Tone → subtle background Tailwind class (badge fills). */
export const TONE_SUBTLE_BG: Record<DisplayTone, string> = {
  accent: 'bg-[var(--accent-subtle)]',
  success: 'bg-[var(--success-subtle)]',
  danger: 'bg-[var(--danger-subtle)]',
  muted: 'bg-white/[0.04]',
  neutral: 'bg-white/[0.04]',
  secondary: 'bg-white/[0.04]',
  teal: 'bg-[color-mix(in_srgb,var(--teal)_14%,transparent)]',
};

export interface KindMeta {
  label: MessageDescriptor;
  color: string;
}

export const KIND_META: Record<ProposalKind, KindMeta> = {
  [ProposalKind.SystemPrompt]: { label: msg`Prompt rewrite`, color: 'var(--accent-primary)' },
  [ProposalKind.Tool]:         { label: msg`Tool update`,    color: 'var(--success)' },
  [ProposalKind.ModelSwitch]:  { label: msg`Model swap`,     color: 'var(--teal)' },
};
