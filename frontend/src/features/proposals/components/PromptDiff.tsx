import { Trans } from '@lingui/react/macro';
import type { SystemPromptDetailsDto } from '../../../api/models';
import { cn } from '../../../lib/cn';
import { buildPromptDiff } from '../proposalsMeta';

type DiffKind = 'same' | 'add' | 'del';

// Per-line diff styling. Each branch is a static token, selected by the
// (data-driven) line kind — byte-identical to the previous inline values.
const LINE_TEXT: Record<DiffKind, string> = {
  add: cn('text-success'),
  del: cn('text-danger'),
  same: cn('text-secondary'),
};
const LINE_BG: Record<DiffKind, string> = {
  add: cn('bg-[color-mix(in_srgb,var(--success)_8%,transparent)]'),
  del: cn('bg-[color-mix(in_srgb,var(--danger)_8%,transparent)]'),
  same: '',
};
const SIGIL_TEXT: Record<DiffKind, string> = {
  add: cn('text-success'),
  del: cn('text-danger'),
  same: cn('text-muted'),
};

interface PromptDiffProps {
  before: string;
  after: string;
}

function PromptDiffView({ before, after }: PromptDiffProps) {
  const rendered = buildPromptDiff(before, after);
  const adds = rendered.filter(r => r.kind === 'add').length;
  const dels = rendered.filter(r => r.kind === 'del').length;

  return (
    <div className="bg-black/40 rounded-md overflow-hidden border border-border-subtle" data-testid="prompt-diff">
      <div className="flex items-center gap-2.5 px-3.5 py-2 border-b border-hairline bg-card-2/30">
        <span className="text-caption text-secondary font-semibold uppercase tracking-[0.07em]"><Trans>System prompt</Trans></span>
        <span className="mono text-body-sm text-success">+{adds}</span>
        <span className="mono text-body-sm text-danger">−{dels}</span>
      </div>
      <div className="mono text-body leading-[1.65]">
        {rendered.map((r, i) => {
          const sigil = r.kind === 'add' ? '+' : r.kind === 'del' ? '−' : ' ';
          return (
            <div key={i} className={cn('flex py-px', LINE_BG[r.kind])}>
              <span className="text-caption text-right select-none shrink-0 text-muted w-9 pl-3.5 pr-2">{i + 1}</span>
              <span className={cn('font-bold shrink-0 text-center w-[18px]', SIGIL_TEXT[r.kind])}>{sigil}</span>
              <span className={cn('flex-1 whitespace-pre-wrap break-words pr-3.5', LINE_TEXT[r.kind])}>{r.text || ' '}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

interface Props {
  details: SystemPromptDetailsDto;
}

export function SystemPromptSection({ details }: Props) {
  return <PromptDiffView before={details.currentSystemMessage} after={details.proposedSystemMessage}/>;
}
