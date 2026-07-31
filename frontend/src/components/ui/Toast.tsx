import { useCallback, useEffect, useRef, useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { XIcon, ArrowUpRightIcon } from '../icons';
import ToastContext, { type ToastAction, type ToastOptions, type ToastItem } from '../../contexts/ToastContext';
import { cn } from '../../lib/cn';
import { FOCUS_RING } from '../../lib/constants';
import { canViewErrorLog, navigateToErrorLog } from '../../lib/errorLogNav';

type ToastType = ToastItem['type'];

/** How long a non-error toast stays up. An actionable one has to survive being read *and* decided
 *  on, which 3s does not cover; a plain confirmation does not need the extra dwell. */
const DISMISS_MS = 3000;
const DISMISS_WITH_ACTION_MS = 8000;

// Finite-state styling (DESIGN §6): each type maps to a fixed border + text class.
// Tokens resolve to the same CSS vars as the previous inline typeColor():
//   success -> var(--success), error -> var(--danger), info -> var(--accent-primary).
// Border keeps the identical 32% color-mix so pixels are unchanged.
const TOAST_BORDER: Record<ToastType, string> = {
  success: cn('border-[color-mix(in_srgb,var(--success)_32%,transparent)]'),
  error: cn('border-[color-mix(in_srgb,var(--danger)_32%,transparent)]'),
  info: cn('border-[color-mix(in_srgb,var(--accent-primary)_32%,transparent)]'),
};
const TOAST_TEXT: Record<ToastType, string> = {
  success: cn('text-success'),
  error: cn('text-danger'),
  info: cn('text-accent'),
};

/** The follow-up link shared by the error deep-link and the generic toast action. */
const TOAST_LINK_CLS = cn(
  'inline-flex items-start gap-1.5 text-left cursor-pointer',
  'rounded-sm hover:underline underline-offset-2 transition-colors',
  FOCUS_RING,
);

/**
 * The toast's follow-up link. Lives here rather than in the two render branches because the plain
 * and error layouts are structurally different but must offer the action identically.
 */
function ToastActionLink(
  { action, onDone, className }: { action: ToastAction; onDone: () => void; className?: string },
) {
  return (
    <button
      type="button"
      onClick={() => { action.onClick(); onDone(); }}
      data-testid="toast-action-btn"
      className={cn(TOAST_LINK_CLS, 'mt-1.5 text-body-sm font-semibold', className)}
    >
      <span>{action.label}</span>
      <ArrowUpRightIcon size={12} strokeWidth={1.75} className="shrink-0 mt-0.5" />
    </button>
  );
}

let globalShow: ((message: string, type: ToastItem['type'], options?: ToastOptions) => void) | null = null;

// eslint-disable-next-line react-refresh/only-export-components
export function showToast(message: string, type: ToastItem['type'] = 'info', options?: ToastOptions) {
  globalShow?.(message, type, options);
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const { t } = useLingui();
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextId = useRef(0);

  const show = useCallback((message: string, type: ToastItem['type'] = 'info', options?: ToastOptions) => {
    const id = ++nextId.current;
    setToasts(prev => [...prev, { id, message, type, ...options }]);

    if (type !== 'error') {
      const after = options?.action ? DISMISS_WITH_ACTION_MS : DISMISS_MS;
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), after);
    }
  }, []);

  const dismiss = useCallback((id: number) => {
    setToasts(prev => prev.filter(t => t.id !== id));
  }, []);

  useEffect(() => {
    globalShow = show;
    return () => { globalShow = null; };
  }, [show]);

  const isDev = import.meta.env.DEV;

  return (
    <ToastContext.Provider value={{ show }}>
      {children}
      <div className="fixed bottom-6 right-6 flex flex-col gap-2 z-[100] pointer-events-none">
        {toasts.map(toast => {
          if (toast.type !== 'error') {
            const { action } = toast;
            return (
              <div
                key={toast.id}
                className={cn(
                  'fade-up bg-card rounded-md px-4 py-2.5 text-title font-medium max-w-[320px] shadow-[var(--shadow-float)] border',
                  TOAST_BORDER[toast.type],
                  TOAST_TEXT[toast.type],
                  // Only an actionable toast takes pointer events; a plain one must not sit in
                  // front of whatever it is covering.
                  action && 'pointer-events-auto',
                )}
                data-testid="toast"
              >
                <div>{toast.message}</div>
                {action && <ToastActionLink action={action} onDone={() => dismiss(toast.id)} />}
              </div>
            );
          }

          // The message becomes a deep-link into the Error Log when the backend captured this
          // error (errorId) and the current user can view the Error Log (an admin navigator is
          // registered). Otherwise it's plain text.
          const deepLinkId = toast.errorId && canViewErrorLog() ? toast.errorId : null;

          return (
            <div
              key={toast.id}
              className={cn(
                'fade-up bg-card rounded-lg px-4 py-3 shadow-[var(--shadow-float)] pointer-events-auto max-w-[420px] border',
                TOAST_BORDER[toast.type],
              )}
            >
              <div className="flex items-start gap-2">
                {deepLinkId ? (
                  <button
                    type="button"
                    onClick={() => {
                      navigateToErrorLog(deepLinkId);
                      dismiss(toast.id);
                    }}
                    data-testid="error-toast-view-btn"
                    aria-label={t`View this error in the Error Log`}
                    className={cn(
                      TOAST_LINK_CLS,
                      'flex-1 min-w-0 text-h2 font-semibold leading-snug',
                      TOAST_TEXT[toast.type],
                    )}
                  >
                    <span className="min-w-0">{toast.message}</span>
                    <ArrowUpRightIcon size={13} strokeWidth={1.75} className="shrink-0 mt-0.5" />
                  </button>
                ) : (
                  <span className={cn('flex-1 text-h2 font-semibold min-w-0 leading-snug', TOAST_TEXT[toast.type])}>
                    {toast.message}
                  </span>
                )}
                <button
                  onClick={() => dismiss(toast.id)}
                  className="text-muted hover:text-primary transition-colors leading-none cursor-pointer shrink-0 mt-0.5"
                  aria-label={t`Dismiss`}
                >
                  <XIcon size={16} strokeWidth={1.5} />
                </button>
              </div>
              {toast.action && (
                <ToastActionLink
                  action={toast.action}
                  onDone={() => dismiss(toast.id)}
                  className={TOAST_TEXT[toast.type]}
                />
              )}
              {isDev && toast.stacktrace && (
                <details className="mt-2">
                  <summary className="text-body-sm text-muted cursor-pointer hover:text-secondary transition-colors select-none">
                    <Trans>Stacktrace</Trans>
                  </summary>
                  <pre className="mt-1 text-body-sm text-muted font-mono whitespace-pre-wrap overflow-x-auto max-h-[200px] overflow-y-auto">
                    {toast.stacktrace}
                  </pre>
                </details>
              )}
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}
