import { useNavigate } from 'react-router';
import { Trans, Plural, useLingui } from '@lingui/react/macro';
import { Avatar } from '../ui/Avatar';
import { Menu } from '../ui/Menu';
import { RowButton } from '../ui/RowButton';
import { CheckIcon, ChevronUpIcon, SettingsIcon } from '../icons';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useCurrentUser } from '../../auth/useCurrentUser';
import type { ProjectDto } from '../../api/models';
import { projectColor } from '../../lib/colors';

function projectInitials(name: string) {
  const trimmed = name.trim();
  if (!trimmed) return '··';
  const parts = trimmed.split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[1][0]).toUpperCase();
}

export function ProjectSelector({ collapsed }: { collapsed: boolean }) {
  const { projects, currentProject, setCurrentProjectId } = useCurrentProject();
  const navigate = useNavigate();
  const { t } = useLingui();
  const isAdmin = useCurrentUser()?.role === 'Admin';

  const name = currentProject?.name ?? t`No project`;
  const memberCount = currentProject?.memberCount ?? 0;
  const color = currentProject ? projectColor(currentProject.id) : 'var(--teal)';

  return (
    <Menu
      side="top"
      align="start"
      trigger={
        <RowButton
          data-testid="project-switcher"
          className={`flex items-center gap-2.5 rounded-lg p-1 hover:bg-white/[.04] transition-colors ${collapsed ? 'justify-center' : 'justify-start'}`}
        >
          <Avatar
            // eslint-disable-next-line lingui/no-unlocalized-strings -- avatar initials placeholder, not UI copy
            initials={currentProject ? projectInitials(name) : 'DP'}
            color={color}
            className="w-7 h-7 rounded-md text-body-sm font-semibold"
          />
          {!collapsed && (
            <>
              <div className="flex-1 min-w-0 text-left">
                <div className="text-body-sm font-semibold truncate">{name}</div>
                <div className="text-body-sm text-muted">
                  <Plural value={memberCount} one="# member" other="# members" />
                </div>
              </div>
              <ChevronUpIcon size={12} className="text-muted shrink-0" />
            </>
          )}
        </RowButton>
      }
    >
      <div className="px-3 pt-2 pb-1 text-caption font-semibold tracking-[0.08em] text-secondary uppercase">
        <Trans>Switch project</Trans>
      </div>
      {projects.length === 0 && <div className="px-3 py-2 text-body text-muted"><Trans>No projects</Trans></div>}
      {projects.map((p) => {
        const active = p.id === currentProject?.id;
        return (
          <Menu.Item
            key={p.id}
            data-testid={`project-switcher-option-${p.id}`}
            onSelect={() => setCurrentProjectId(p.id)}
            icon={
              <Avatar
                initials={projectInitials(p.name)}
                color={projectColor(p.id)}
                className="w-6 h-6 rounded-md text-caption font-semibold"
              />
            }
          >
            <span className="flex-1 min-w-0 truncate">{p.name}</span>
            {active && <CheckIcon size={14} strokeWidth={2.5} className="text-accent shrink-0" />}
          </Menu.Item>
        );
      })}
      {isAdmin && (
        <>
          <Menu.Separator />
          <Menu.Item icon={<SettingsIcon size={14} />} onSelect={() => navigate('/settings')}><Trans>Settings</Trans></Menu.Item>
        </>
      )}
    </Menu>
  );
}

export type { ProjectDto };
