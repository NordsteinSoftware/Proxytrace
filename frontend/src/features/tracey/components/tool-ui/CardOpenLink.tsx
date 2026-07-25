import { Link } from 'react-router';
import { useLingui } from '@lingui/react/macro';
import { ArrowUpRightIcon } from '../../../../components/icons';

/** A small "Open" link for a card's top-right corner (passed as ToolUIFrame's cornerAccessory). */
export function CardOpenLink({ to, label }: { to: string; label?: string }) {
  const { t } = useLingui();
  return (
    <Link
      to={to}
      className="inline-flex items-center gap-1 rounded-sm text-body-sm text-muted transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]"
    >
      {label ?? t`Open`}
      <ArrowUpRightIcon size={13} />
    </Link>
  );
}
