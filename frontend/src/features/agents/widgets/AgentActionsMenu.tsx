import { useLingui } from '@lingui/react/macro';
import { useNavigate } from 'react-router';
import { Menu } from '../../../components/ui/Menu';
import { IconButton } from '../../../components/ui/Button';
import { MoreHorizontalIcon, PlayIcon, ActivityIcon, SparklesIcon } from '../../../components/icons';

interface Props {
  agentId: string;
}

export function AgentActionsMenu({ agentId }: Props) {
  const { t } = useLingui();
  const navigate = useNavigate();

  const items = [
    { id: 'open-in-playground', label: t`Open in playground`, icon: <PlayIcon size={13} />, to: `/playground?agentId=${agentId}` },
    { id: 'view-traces', label: t`View traces`, icon: <ActivityIcon size={13} />, to: `/traces?agentId=${agentId}` },
    { id: 'view-proposals', label: t`View proposals`, icon: <SparklesIcon size={13} />, to: `/proposals?agentId=${agentId}` },
  ];

  return (
    <Menu
      trigger={
        <IconButton aria-label={t`More actions`} data-testid="agent-actions-btn">
          <MoreHorizontalIcon size={15} />
        </IconButton>
      }
    >
      {items.map(item => (
        <Menu.Item
          key={item.id}
          icon={<span className="text-muted shrink-0">{item.icon}</span>}
          onSelect={() => navigate(item.to)}
          data-testid={`agent-action-${item.id}`}
        >
          {item.label}
        </Menu.Item>
      ))}
    </Menu>
  );
}
