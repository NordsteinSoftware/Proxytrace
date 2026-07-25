import { useState, lazy, Suspense } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Trans } from '@lingui/react/macro';
import { ErrorBoundary } from '../ErrorBoundary';
import { ChromeErrorFallback } from './ChromeErrorFallback';
import { Sidebar } from './Sidebar';
import { Topbar } from './Topbar';
import { GracePeriodBanner } from '../license/GracePeriodBanner';
import { InvalidLicenseBanner } from '../license/InvalidLicenseBanner';
import { QuotaBanner } from '../license/QuotaBanner';
import { UpdateBanner } from '../updates/UpdateBanner';
import { useIsMobile } from '../../hooks/useMediaQuery';

// The Tracey chat stack (assistant-ui + ai SDK + tools + docs index) is heavy; loading it
// lazily keeps it out of the main chunk so first paint of every page is faster.
const TraceyHost = lazy(() => import('../../features/tracey/TraceyHost'));

/** App frame: navigation rail + top bar + the routed page. Orchestration only — the rail and bar
 *  own their own data; the Shell owns just the collapse/drawer state and the page scroll container. */
export function Shell() {
  // Start collapsed on narrow viewports (laptops below 1280px) so page content gets the width;
  // the user can still expand manually. Initial-render check only — no resize listener, so a
  // deliberate toggle is never fought.
  const [collapsed, setCollapsed] = useState(() => window.matchMedia('(max-width: 1279px)').matches);
  // Below md the sidebar is an off-canvas drawer instead of an inline rail.
  const isMobile = useIsMobile();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  // The chrome boundaries below reset on navigation. `key` (not `pathname`) because several
  // selections live in the query string — `?notification=A` → `?notification=B` must clear too.
  const location = useLocation();

  const toggleSidebar = () => (isMobile ? setMobileNavOpen(v => !v) : setCollapsed(c => !c));

  return (
    // Transparent — the flat body surface is the app background; the panes sit directly on it.
    <div className="flex w-full h-screen overflow-hidden bg-transparent relative z-[1]">
      {/* Each chrome region gets its own boundary. The rail and the masthead render OUTSIDE the
          router `Outlet`, so the route-level boundary in `AppRoutes` structurally cannot catch a
          throw here — without these, one broken widget (a bell, the search box, a banner) unmounts
          the whole React root and the page goes blank with no recovery but a reload. Per-region
          rather than one boundary at the root, so a failure degrades that region and the rest of
          the app stays navigable. */}
      <ErrorBoundary resetKeys={[location.key]} fallback={<ChromeErrorFallback className="shrink-0 w-16 border-r border-border max-md:hidden" />}>
        <Sidebar
          collapsed={collapsed}
          mobileNavOpen={mobileNavOpen}
          onMobileNavClose={() => setMobileNavOpen(false)}
        />
      </ErrorBoundary>

      {/* Main area */}
      <div className="flex flex-col flex-1 overflow-hidden min-w-0">
        {/* Two boundaries, not one: the four banners are decorative and safe to lose, but `Topbar`
            carries the only mobile navigation affordance (the hamburger — the sidebar is off-canvas
            below md). A banner render throw must not collapse the masthead and drop the hamburger,
            stranding a mobile user with no nav. Same region-granularity reasoning that split the
            rail, masthead, and page area, one level deeper. */}
        <ErrorBoundary resetKeys={[location.key]} fallback={<ChromeErrorFallback className="h-[48px] shrink-0 bg-surface-2 border-b border-border" />}>
          <InvalidLicenseBanner />
          <GracePeriodBanner />
          <QuotaBanner />
          <UpdateBanner />
        </ErrorBoundary>
        <ErrorBoundary resetKeys={[location.key]} fallback={<ChromeErrorFallback className="h-[48px] shrink-0 bg-surface-2 border-b border-border" />}>
          <Topbar onToggleSidebar={toggleSidebar} />
        </ErrorBoundary>

        {/* Page content — single vertical scroll container for the app */}
        {/* The rail and the masthead are flat, full-bleed panes now, so this pane carries no
            margin — the rail's `border-r` and the topbar's `border-b` are the only dividers, with
            no gutter between them. The 10px breathing room moved from margin to padding, so the
            scroll container itself stays flush on every edge. That also keeps the reason the right
            inset was padding in the first place: Firefox/Linux uses overlay scrollbars, so
            `scrollbar-gutter: stable` does NOT reserve space — the thumb would paint over the
            content. Flush container + `p-[10px]` lets the overlay thumb float in the padding strip
            without overlapping cards, while staying glued to the screen edge. */}
        <main className="flex-1 min-h-0 overflow-y-auto overflow-x-hidden p-[10px] bg-transparent relative z-0 flex flex-col">
          {/* TraceyHost mounts the chat runtime here — above the router `Outlet`, not on the
              Tracey page — so the conversation survives navigation. It is lazy: while its chunk
              loads (in parallel with the route chunk) the page area shows the same loader the
              route Suspense would. */}
          {/* Wraps `TraceyHost` and the lazy import itself, neither of which the per-route
              boundary inside the `Outlet` can reach — a chunk that 404s after a deploy would
              otherwise blank the app rather than just the page area. */}
          <ErrorBoundary resetKeys={[location.key]}>
            <Suspense
              fallback={
                <div className="flex items-center justify-center flex-1 text-muted text-title">
                  <Trans>Loading…</Trans>
                </div>
              }
            >
              <TraceyHost>
                <Outlet />
              </TraceyHost>
            </Suspense>
          </ErrorBoundary>
        </main>
      </div>
    </div>
  );
}
