import { useState } from 'react';
import { useLingui } from '@lingui/react/macro';
import { useQueryClient } from '@tanstack/react-query';
import { Popover } from '../../components/ui/Popover';
import { IconButton } from '../../components/ui/Button';
import { BellIcon } from '../../components/icons';
import { useNotificationStream } from '../../api/event-stream';
import { QUERY_KEYS } from '../../api/query-keys';
import { NotificationStatus } from '../../api/models';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useSelectedId } from '../../hooks/useSelectedId';
import { useNotification, useNotifications, useNotificationMutations } from './hooks/useNotifications';
import { useMarkReadOnOpen } from './hooks/useMarkReadOnOpen';
import { NotificationsPanel } from './components/NotificationsPanel';
import { NotificationDetailDrawer } from './components/NotificationDetailDrawer';

/**
 * Top-bar notifications inbox: a bell `IconButton` with an unread badge that opens a
 * GitHub-style popover panel, plus the detail drawer the rows open. Mounted once in the `Shell`
 * topbar so the badge, live updates, and the `?notification=<id>` deep link all work on every page.
 */
export function NotificationsMenu() {
  const { t } = useLingui();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const { currentProjectId } = useCurrentProject();
  const projectId = currentProjectId ?? undefined;
  const enabled = currentProjectId !== null;

  const { data: notifications, isLoading } = useNotifications(projectId, enabled);
  const { markRead, dismiss } = useNotificationMutations();

  // Which notification the drawer shows lives in the URL, so an emailed `/notifications/<id>` link
  // (redirected to `?notification=<id>`) opens it wherever the user lands.
  // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
  const [selectedId, selectNotification] = useSelectedId('notification');
  const fromList = notifications?.find(n => n.id === selectedId) ?? null;
  // A dismissed notification — or, for a member, a global one — is not in the list; fetch by id.
  const { data: fetched } = useNotification(fromList ? null : selectedId);
  const openNotification = fromList ?? fetched ?? null;
  useMarkReadOnOpen(openNotification, markRead.mutate);

  // Live updates app-wide — refresh the cache on any notification SSE event for this project.
  useNotificationStream(projectId, () => {
    qc.invalidateQueries({ queryKey: QUERY_KEYS.notificationsRoot });
  });

  const rows = notifications ?? [];
  const unreadCount = rows.filter(n => n.status === NotificationStatus.Unread).length;
  const hasUnread = unreadCount > 0;

  // Only the row whose mutation is in flight is disabled — not the whole list.
  const pendingId = (markRead.isPending ? markRead.variables : undefined)
    ?? (dismiss.isPending ? dismiss.variables : undefined)
    ?? null;

  // Prev/next step through the loaded list, so they survive the popover closing. A deep-linked
  // notification that isn't in the list (dismissed) simply has neither.
  const openIndex = openNotification ? rows.findIndex(n => n.id === openNotification.id) : -1;
  const prev = openIndex > 0 ? rows[openIndex - 1] : null;
  const next = openIndex >= 0 && openIndex < rows.length - 1 ? rows[openIndex + 1] : null;

  function openDetail(id: string) {
    // The popover renders above the drawer (z-[80] vs z-50), so it must close for the drawer to
    // be visible. Don't raise the drawer's z-index — every drawer in the app shares it.
    setOpen(false);
    // Close any page-level drawer in the same history replace. This one is global chrome and
    // paints over the page, and two open `DetailPanel`s both bind document keydown — Esc and the
    // arrows would drive both, and their two `setSearchParams` updaters (each derived from the
    // pre-update URL) would clobber one another. `clear` exists for exactly this (see
    // `useSelectedId`); master/detail `?id=` is a pane, not an overlay, so it is left alone.
    // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param keys
    selectNotification(id, ['trace', 'error']);
  }

  function handleDismiss(id: string) {
    dismiss.mutate(id);
    if (id === selectedId) selectNotification(null);
  }

  return (
    <>
      <Popover
        open={open}
        onOpenChange={setOpen}
        align="end"
        className="w-[380px]"
        trigger={
          <IconButton
            data-testid="notifications-menu-trigger"
            aria-label={hasUnread ? t`Notifications (${unreadCount} unread)` : t`Notifications`}
            className="relative"
          >
            <BellIcon size={16} />
            {hasUnread && (
              <span
                data-testid="notifications-unread-badge"
                aria-hidden
                className="absolute -top-0.5 -right-0.5 min-w-[15px] h-[15px] px-1 inline-flex items-center justify-center rounded-none bg-accent text-accent-ink text-caption font-bold leading-none tabular-nums"
              >
                {unreadCount > 9 ? '9+' : unreadCount}
              </span>
            )}
          </IconButton>
        }
      >
        <NotificationsPanel
          notifications={rows}
          isLoading={isLoading}
          unreadCount={unreadCount}
          pendingId={pendingId}
          onOpen={openDetail}
          onMarkRead={markRead.mutate}
          onDismiss={handleDismiss}
        />
      </Popover>

      {openNotification && (
        <NotificationDetailDrawer
          key={openNotification.id}
          notification={openNotification}
          onClose={() => selectNotification(null)}
          onPrev={prev ? () => selectNotification(prev.id) : undefined}
          onNext={next ? () => selectNotification(next.id) : undefined}
          onDismiss={handleDismiss}
        />
      )}
    </>
  );
}
