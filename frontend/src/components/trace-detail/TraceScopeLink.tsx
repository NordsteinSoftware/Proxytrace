import { useNavigate } from 'react-router';
import { useLingui } from '@lingui/react/macro';
import { useProjectScopes } from '../../hooks/useProjectScopes';
import { scopeLabel } from '../../lib/scopes';
import { LayersIcon } from '../icons';
import { Button } from '../ui/Button';

interface Props {
  scopeId: string;
  onClose: () => void;
}

/**
 * Provenance-row link to the scope (use-case group) a trace was sent under — labelled with the
 * scope's name, opening it on the Scopes page. Mirrors the session link beside it.
 */
export function TraceScopeLink({ scopeId, onClose }: Props) {
  const navigate = useNavigate();
  const { t } = useLingui();
  const { byId } = useProjectScopes();
  const scope = byId.get(scopeId);
  return (
    <>
      <span aria-hidden className="text-body-sm text-muted shrink-0">·</span>
      <Button
        variant="link"
        size="sm"
        data-testid="trace-scope-link"
        onClick={() => { onClose(); navigate(`/scopes?id=${scopeId}`); }}
        title={t`Open scope`}
        leftIcon={<LayersIcon size={12} />}
        className="text-body-sm shrink-0 max-w-[160px] truncate"
      >
        {scope ? scopeLabel(scope) : t({ message: 'Scope', context: 'use-case group of agents' })}
      </Button>
    </>
  );
}
