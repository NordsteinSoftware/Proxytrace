import { useEffect, useId, useRef, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { Trans, useLingui } from '@lingui/react/macro';
import { Button, IconButton } from '../ui/Button';
import { XIcon } from '../icons';

type ModalSize = 'sm' | 'md' | 'lg' | 'xl';

const SIZE_PX: Record<ModalSize, number> = { sm: 560, md: 720, lg: 960, xl: 1180 };

/* eslint-disable lingui/no-unlocalized-strings -- CSS focus selectors, not user-facing copy */
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'textarea:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');
/* eslint-enable lingui/no-unlocalized-strings */

/** Visible, focusable descendants of the panel, in DOM (tab) order. */
function getFocusable(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
    el => el.offsetParent !== null || el === document.activeElement,
  );
}

interface ModalProps {
  title?: string;
  onClose: () => void;
  children: React.ReactNode;
  footer?: React.ReactNode;
  headerActions?: React.ReactNode;
  maxWidth?: number;
  size?: ModalSize;
  initialFocusRef?: RefObject<HTMLElement | null>;
}

export function Modal({ title, onClose, children, footer, headerActions, maxWidth, size, initialFocusRef }: ModalProps) {
  const { t } = useLingui();
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const resolvedMaxWidth = maxWidth ?? (size ? SIZE_PX[size] : SIZE_PX.sm);

  // Move focus into the panel on open and restore it to the trigger on close.
  useEffect(() => {
    const panel = panelRef.current;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const focusable = panel ? getFocusable(panel) : [];
    (initialFocusRef?.current ?? focusable[0] ?? panel)?.focus();
    return () => previouslyFocused?.focus?.();
  }, [initialFocusRef]);

  // Close on Esc and trap Tab / Shift+Tab within the panel.
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
        return;
      }
      if (e.key !== 'Tab') return;
      const panel = panelRef.current;
      if (!panel) return;
      const items = getFocusable(panel);
      const active = document.activeElement;
      if (items.length === 0) {
        e.preventDefault();
        panel.focus();
        return;
      }
      const first = items[0];
      const last = items[items.length - 1];
      if (e.shiftKey) {
        if (active === first || active === panel || !panel.contains(active)) {
          e.preventDefault();
          last.focus();
        }
      } else if (active === last || active === panel || !panel.contains(active)) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
  }, [onClose]);

  return createPortal(
    <div className="modal-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? titleId : undefined}
        tabIndex={-1}
        data-testid="modal-panel"
        className="modal-panel fade-up"
        style={{ maxWidth: `min(${resolvedMaxWidth}px, 94vw)`, width: '100%' }}
      >
        {title && (
          <div className="flex items-center justify-between gap-2 mb-5">
            <div className="flex items-center gap-2 min-w-0">
              <h2 id={titleId} className="m-0 text-h2 font-semibold text-primary truncate min-w-0">{title}</h2>
              {headerActions}
            </div>
            <IconButton onClick={onClose} aria-label={t`Close`}><XIcon size={14} /></IconButton>
          </div>
        )}
        {children}
        {footer && (
          <div className="mt-5 flex justify-end gap-2">
            {footer}
          </div>
        )}
      </div>
    </div>,
    document.body,
  );
}

interface ModalFooterProps {
  onCancel: () => void;
  onSubmit: () => void;
  submitLabel: string;
  loading?: boolean;
  disabled?: boolean;
  danger?: boolean;
}

export function ModalFooter({ onCancel, onSubmit, submitLabel, loading, disabled, danger }: ModalFooterProps) {
  return (
    <>
      <Button variant="ghost" onClick={onCancel}><Trans>Cancel</Trans></Button>
      <Button
        variant={danger ? 'danger' : 'primary'}
        data-testid="modal-submit"
        onClick={onSubmit}
        loading={loading}
        disabled={disabled}
      >
        {submitLabel}
      </Button>
    </>
  );
}
