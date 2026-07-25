import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { Card } from '../../../../components/ui/Card';
import { Badge } from '../../../../components/ui/Badge';
import { agentColor } from '../../../../lib/colors';
import { useAgents } from '../../../agents/hooks/useAgents';
import type { CustomAnomalyDetectorDto } from '../../../../api/models';
import { TRIGGER_KIND_LABEL } from '../detectors';
import { DetectorDetailHeader } from './DetectorDetailHeader';

interface Props {
  detector: CustomAnomalyDetectorDto;
  onEdit: () => void;
  onDelete: () => void;
  onToggleEnabled: (next: boolean) => void;
  toggling: boolean;
}

/** Full detail view for one detector: header, review instructions, triggers, and agent scope. */
export function DetectorDetail({ detector: d, onEdit, onDelete, onToggleEnabled, toggling }: Props) {
  const navigate = useNavigate();
  const { i18n } = useLingui();
  const { allAgents } = useAgents();
  const nameById = new Map(allAgents.map(a => [a.id, a.name]));

  return (
    <div data-testid="detector-detail" className="fade-up flex flex-col gap-3.5 @container">
      <DetectorDetailHeader
        detector={d}
        onEdit={onEdit}
        onDelete={onDelete}
        onToggleEnabled={onToggleEnabled}
        toggling={toggling}
      />

      <Card>
        <Card.Header
          title={<Trans>Review instructions</Trans>}
          description={<Trans>What the judge model looks for in a matched call.</Trans>}
        />
        <Card.Body>
          <p className="text-body text-secondary whitespace-pre-wrap m-0" data-testid="detector-instructions">
            {d.instructions}
          </p>
        </Card.Body>
      </Card>

      <div className="grid grid-cols-1 @3xl:grid-cols-2 gap-3.5 items-start">
        <Card>
          <Card.Header
            title={<Trans>Triggers</Trans>}
            description={<Trans>A call is sent to the judge only when one of these matches.</Trans>}
          />
          <Card.Body className="flex flex-col gap-1.5" data-testid="detector-detail-triggers">
            {d.triggers.map((trigger, i) => (
              <div key={i} className="flex items-center gap-2.5 px-2.5 py-1.5 rounded-md bg-card-2">
                <Badge variant="tinted" color="var(--teal)" label={i18n._(TRIGGER_KIND_LABEL[trigger.kind])} />
                <code className="font-mono text-body-sm text-secondary truncate" title={trigger.pattern}>
                  {trigger.pattern}
                </code>
              </div>
            ))}
          </Card.Body>
        </Card>

        <Card>
          <Card.Header
            title={<Trans>Agent scope</Trans>}
            description={d.allAgents
              ? <Trans>This detector reviews calls from every agent in the project.</Trans>
              : <Trans>This detector only reviews calls from the agents below.</Trans>}
          />
          <Card.Body className="flex flex-wrap gap-1.5" data-testid="detector-detail-scope">
            {d.allAgents ? (
              <Badge variant="accent" label={<Trans>All agents</Trans>} />
            ) : (
              d.agentIds.map(id => (
                <Badge
                  key={id}
                  variant="tinted"
                  color={agentColor(id)}
                  dot
                  label={nameById.get(id) ?? <Trans>Removed agent</Trans>}
                  onClick={() => navigate(`/agents?id=${encodeURIComponent(id)}`)}
                />
              ))
            )}
          </Card.Body>
        </Card>
      </div>
    </div>
  );
}
