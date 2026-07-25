import { Outlet } from 'react-router';
import { SettingsNav } from './components/SettingsNav';

/**
 * The settings hub. A persistent left sub-nav (grouped into project- vs workspace-scoped
 * sections) beside a scrolling content panel that renders the active section via <Outlet/>.
 * The whole area is admin-only — gated at the route in App.tsx.
 *
 * The content panel mirrors the app shell's <main> (`flex-1 min-h-0 overflow-y-auto flex-col`)
 * so hosted pages keep the exact height behaviour they had as top-level routes: Providers'
 * `flex-1 min-h-0` master/detail, ErrorLog's `h-full`, and Users' natural flow all just work.
 */
export default function SettingsLayout() {
  return (
    <div className="w-full min-w-0 flex-1 min-h-0 flex gap-5" data-testid="settings">
      <SettingsNav />
      <div className="flex-1 min-w-0 min-h-0 overflow-y-auto flex flex-col">
        <Outlet />
      </div>
    </div>
  );
}
