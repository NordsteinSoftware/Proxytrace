import { useLingui } from '@lingui/react/macro';
import type { ApiKeyScope } from '../../../api/models';
import { cn } from '../../../lib/cn';
import { SCOPE_LETTER, SCOPE_ORDER } from '../keyScopes';

const GRANTED: Record<ApiKeyScope, string> = {
  Ingestion: cn('border-transparent bg-[color-mix(in_srgb,var(--text-secondary)_16%,transparent)] text-primary'),
  Passthrough: cn('border-[color-mix(in_srgb,var(--warn)_30%,transparent)] bg-[var(--warn-subtle)] text-[var(--warn)]'),
  McpRead: cn('border-[color-mix(in_srgb,var(--accent-primary)_30%,transparent)] bg-[var(--accent-subtle)] text-[var(--accent-hover)]'),
  McpWrite: cn('border-[color-mix(in_srgb,var(--warn)_30%,transparent)] bg-[var(--warn-subtle)] text-[var(--warn)]'),
  ApiRead: cn('border-[color-mix(in_srgb,var(--teal)_30%,transparent)] bg-[color-mix(in_srgb,var(--teal)_14%,transparent)] text-[var(--teal)]'),
  ApiWrite: cn('border-[color-mix(in_srgb,var(--success)_30%,transparent)] bg-[var(--success-subtle)] text-[var(--success)]'),
};

const GHOST = cn('border-hairline bg-transparent text-muted opacity-55');

/**
 * Compact, fixed-width permission triplet for an API key — Proxy / MCP read / MCP write as monospace
 * chips, granted ones tinted, the rest ghosted. A single line at a constant width so it never inflates
 * the row, and reads as a glanceable least-privilege profile.
 */
export function KeyCapabilities({ scopes }: { scopes: ApiKeyScope[] }) {
  const { t } = useLingui();
  const names: Record<ApiKeyScope, string> = {
    Ingestion: t`Ingestion proxy`,
    Passthrough: t`Upstream pass-through`,
    McpRead: t`MCP read`,
    McpWrite: t`MCP write`,
    ApiRead: t`REST API read`,
    ApiWrite: t`REST API write`,
  };
  return (
    <div className="inline-flex items-center gap-1" data-testid="key-capabilities">
      {SCOPE_ORDER.map(scope => {
        const on = scopes.includes(scope);
        return (
          <span
            key={scope}
            data-testid={`key-cap-${scope}`}
            data-on={on}
            title={names[scope]}
            className={cn(
              'inline-grid place-items-center w-[18px] h-[18px] rounded-sm border font-mono text-caption font-bold leading-none select-none',
              on ? GRANTED[scope] : GHOST,
            )}
          >
            {SCOPE_LETTER[scope]}
          </span>
        );
      })}
    </div>
  );
}
