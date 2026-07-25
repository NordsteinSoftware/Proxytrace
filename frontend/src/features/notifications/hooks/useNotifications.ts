import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { notificationsApi } from '../../../api/notifications';
import { QUERY_KEYS } from '../../../api/query-keys';

/**
 * Non-dismissed notifications for the current project scope (newest first).
 *
 * `throwOnError: false` overrides the global default (`app/queryClient.ts`). The bell renders in
 * the topbar on every route, so a rethrown fetch error would unmount the whole app — a failed
 * inbox has to degrade to an empty bell instead. `api/client.ts` still raises the error toast, so
 * the failure is not silent. (The chrome `ErrorBoundary` in `Shell` is the backstop, not the
 * mechanism: a query that can fail routinely should not be trading on it.)
 */
export function useNotifications(projectId?: string, enabled = true) {
  return useQuery({
    queryKey: QUERY_KEYS.notifications(projectId),
    queryFn: () => notificationsApi.list({ projectId, includeRead: true }),
    enabled,
    throwOnError: false,
  });
}

/**
 * A single notification by id, for the `?notification=` deep link. The list can't always resolve
 * it — dismissed rows are excluded and a member never sees global rows — so the drawer falls back
 * to this by-id fetch. A stale id (deleted project) 404s silently and simply yields no drawer.
 */
export function useNotification(id: string | null) {
  return useQuery({
    queryKey: QUERY_KEYS.notification(id ?? undefined),
    queryFn: () => {
      if (!id) throw new Error('no notification id');
      return notificationsApi.get(id, { silentStatuses: [404] });
    },
    enabled: !!id,
    throwOnError: false,
    retry: false,
  });
}

/** Mark-read / dismiss mutations. Both invalidate the notifications cache on success. */
export function useNotificationMutations() {
  const qc = useQueryClient();
  const invalidate = () => qc.invalidateQueries({ queryKey: QUERY_KEYS.notificationsRoot });

  const markRead = useMutation({
    // A 409 means the row was dismissed meanwhile (opening a notification marks it read, so this
    // races with a concurrent dismiss). That's an expected outcome, not an error toast.
    mutationFn: (id: string) => notificationsApi.markRead(id, { silentStatuses: [409] }),
    onSuccess: invalidate,
  });

  const dismiss = useMutation({
    mutationFn: (id: string) => notificationsApi.dismiss(id),
    onSuccess: invalidate,
  });

  return { markRead, dismiss };
}
