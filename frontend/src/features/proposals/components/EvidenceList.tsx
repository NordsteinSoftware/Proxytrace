import { Link } from 'react-router';
import { Trans, Plural } from '@lingui/react/macro';
import { ChevronRightIcon, ExternalLinkIcon } from '../../../components/icons';
import { cn } from '../../../lib/cn';
import { Card } from '../../../components/ui/Card';

interface Props {
  ids: string[];
}

export function EvidenceList({ ids }: Props) {
  return (
    <Card elevation="raised" padding="none" className="overflow-hidden" data-testid="evidence-list">
      <div className="flex items-center gap-2 px-3.5 py-2.5 border-b border-hairline">
        <span className="text-title font-semibold"><Trans>Evidence</Trans></span>
        <span className="text-body-sm text-muted">· <Plural value={ids.length} one="# failing run motivated this" other="# failing runs motivated this" /></span>
      </div>
      {ids.map((id, i) => (
        <Link
          key={id}
          to={`/runs?run=${id}`}
          className={cn(
            'grid grid-cols-[8px_1fr_auto_auto] w-full items-center gap-2.5 px-3.5 py-2.5 hover:bg-card-2/40 transition-colors',
            i !== 0 && 'border-t border-hairline',
          )}
        >
          <span className="size-1.5 rounded-full bg-warn"/>
          <div className="min-w-0">
            <div className="text-title font-medium mb-0.5 text-primary"><Trans>Test run {id.slice(0, 8)}</Trans></div>
            <div className="text-body-sm text-muted"><Trans>Captured failing trace cluster</Trans></div>
          </div>
          <span className="mono text-caption text-muted">{id.slice(0, 8)}</span>
          <span className="text-muted inline-flex items-center gap-1 text-caption">
            <ExternalLinkIcon size={11}/>
            <ChevronRightIcon size={12}/>
          </span>
        </Link>
      ))}
    </Card>
  );
}
