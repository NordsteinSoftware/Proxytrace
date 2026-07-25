import { useCallback, useRef } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { LanguageMenuItems } from './LanguageMenuItems';
import { EmailNotificationMenuItems } from './EmailNotificationMenuItems';
import { navItems, HEALTH_CHIP, HEALTH_DOT, HEALTH_LABEL, type HealthStatus } from './shellNav';
import { useCurrentUser } from '../../auth/useCurrentUser';
import { useAuthMode } from '../../auth/authMode';
import { useHealth } from '../../hooks/useHealth';
import { useGlobalShortcut } from '../../hooks/useGlobalShortcut';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useKiosk } from '../../contexts/KioskContext';
import { LicenseBadge } from '../license/LicenseBadge';
import { NotificationsMenu } from '../../features/notifications/NotificationsMenu';
import { Avatar } from '../ui/Avatar';
import { IconButton } from '../ui/Button';
import { Menu } from '../ui/Menu';
import { UnifiedSearch, type UnifiedSearchHandle } from '../search/UnifiedSearch';
import { LayoutSidebarIcon, LogOutIcon, LockIcon } from '../icons';
import { cn } from '../../lib/cn';

// Layout variant token (not UI copy) — typed so the literal is recognized as non-copy.
const SEARCH_WIDTH: 'auto' | 'fixed' = 'fixed';

interface TopbarProps {
  /** Collapse the rail (md+) or toggle the off-canvas drawer (below md). Owned by the Shell. */
  onToggleSidebar: () => void;
}

/** App top bar — breadcrumb, global search (⌘K), health chip, license badge, notifications, user menu.
 *  Owns its own search ref + shortcut and derives the active page from the route. */
export function Topbar({ onToggleSidebar }: TopbarProps) {
  const { t, i18n } = useLingui();
  const location = useLocation();
  const navigate = useNavigate();
  const { data: online } = useHealth();
  const { data: authMode } = useAuthMode();
  const { currentProject } = useCurrentProject();
  // interactive == full read-write kiosk; the kiosk has no account, so no account-security menu.
  const { enabled: kioskEnabled } = useKiosk();
  const currentUser = useCurrentUser();

  const searchRef = useRef<UnifiedSearchHandle>(null);
  const focusSearch = useCallback(() => searchRef.current?.focus(), []);
  // eslint-disable-next-line lingui/no-unlocalized-strings -- keyboard shortcut key, not UI copy
  useGlobalShortcut('k', focusSearch);

  // The account-security (MFA) page only applies to real local-auth sessions — not OIDC (managed by
  // the IdP) and not the login-free kiosk.
  const showAccountMenu = authMode?.mode === 'local' && !kioskEnabled;

  const healthStatus: HealthStatus = online === true ? 'online' : online === false ? 'offline' : 'connecting';
  const activeNavEntry = [...navItems]
    .sort((a, b) => b.to.length - a.to.length)
    .find(n => location.pathname === n.to || location.pathname.startsWith(n.to + '/'));
  const pageLabel = activeNavEntry ? i18n._(activeNavEntry.label) : t`Dashboard`;
  const userName = currentUser?.email ?? t`User`;
  const userInitials = userName
    .split(/[@.\s_-]+/)
    .filter(Boolean)
    .map(part => part.charAt(0).toUpperCase())
    .join('')
    // eslint-disable-next-line lingui/no-unlocalized-strings -- avatar initials fallback, not UI copy
    .slice(0, 2) || 'U';

  return (
    <header
      className="h-[48px] shrink-0 flex items-center px-4 gap-3 relative z-[3] bg-surface-2 border-b border-border"
    >
      <IconButton onClick={onToggleSidebar} aria-label={t`Toggle sidebar`}>
        <LayoutSidebarIcon size={16} />
      </IconButton>

      <div className="flex items-center gap-2 text-title min-w-0 shrink whitespace-nowrap font-mono">
        <span className="text-muted truncate max-w-[180px] hidden md:inline">{currentProject?.name ?? '—'}</span>
        <span className="text-muted hidden md:inline">/</span>
        <span className="font-semibold truncate">{pageLabel}</span>
      </div>

      {/* Search needs real width to be usable — below sm it yields to the page title. */}
      <div className="flex-1 min-w-0 hidden sm:block">
        {currentProject?.id ? (
          <UnifiedSearch ref={searchRef} projectId={currentProject.id} width={SEARCH_WIDTH} />
        ) : (
          <div className="flex-1 max-w-[720px] mx-auto" />
        )}
      </div>
      <div className="flex-1 sm:hidden" />

      <div
        title={i18n._(HEALTH_LABEL[healthStatus])}
        className={cn(
          'flex items-center gap-1.5 px-2.5 py-1.5 rounded-none border text-body-sm font-semibold whitespace-nowrap shrink-0',
          HEALTH_CHIP[healthStatus],
        )}
      >
        <span
          className={cn(
            'size-[6px] rounded-full inline-block',
            HEALTH_DOT[healthStatus],
            healthStatus === 'online' && 'pulse-dot',
          )}
        />
        {/* Dot-only below lg — the label is redundant with the color + title on tight topbars. */}
        <span className="hidden lg:inline">{i18n._(HEALTH_LABEL[healthStatus])}</span>
      </div>

      <span className="hidden sm:contents"><LicenseBadge /></span>

      <NotificationsMenu />

      <Menu
        trigger={
          <IconButton
            data-testid="user-menu-trigger"
            title={userName}
            aria-label={t`User menu (${userName})`}
          >
            <Avatar initials={userInitials} color="var(--accent-primary)" className="w-[30px] h-[30px] rounded-full text-body-sm font-semibold" />
          </IconButton>
        }
      >
        <EmailNotificationMenuItems />
        <LanguageMenuItems />
        {showAccountMenu && (
          <Menu.Item
            data-testid="account-security-btn"
            icon={<LockIcon size={15} />}
            onSelect={() => navigate('/account')}
          >
            <Trans>Account security</Trans>
          </Menu.Item>
        )}
        <Menu.Item
          data-testid="logout-btn"
          icon={<LogOutIcon size={15} />}
          onSelect={() => currentUser?.signOut()}
        >
          <Trans>Logout</Trans>
        </Menu.Item>
      </Menu>
    </header>
  );
}
