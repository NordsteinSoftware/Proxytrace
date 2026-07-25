import { Link } from 'react-router';
import { Trans, Plural } from '@lingui/react/macro';
import { useLicense } from '../../hooks/useLicense';
import { LockIcon } from '../icons';
import { daysLeft } from './licenseUtils';

/**
 * Slim warning bar pinned above the top bar while the license is in its offline
 * grace window. Communicates how long the install will keep working before it
 * falls back to the Free tier.
 */
export function GracePeriodBanner() {
  const { data } = useLicense();
  if (!data || data.status !== 'grace') return null;

  const remaining = daysLeft(data.gracePeriodEndsAt);

  return (
    <div
      data-testid="license-grace-banner"
      role="status"
      className="shrink-0 flex items-center gap-2 px-4 py-1.5 text-body-sm font-medium text-warn bg-warn-subtle border-b border-[color-mix(in_srgb,var(--warn)_25%,transparent)]"
    >
      <LockIcon size={14} />
      <span>
        <Trans>
          License server unreachable — running on cached license. Enterprise features
          stay active for{' '}
          <Plural value={remaining} one="# more day" other="# more days" />.
        </Trans>
      </span>
      <Link to="/upgrade" className="ml-auto underline underline-offset-2 hover:text-primary">
        <Trans>Details</Trans>
      </Link>
    </div>
  );
}
