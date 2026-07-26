import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes, useLocation, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { LocaleSync } from '../i18n/LocaleSync';
import { Shell } from '../components/layout/Shell';
import { ErrorBoundary } from '../components/ErrorBoundary';
import ProjectProvider from '../contexts/ProjectProvider';
import { RequiresFeature } from '../components/license/RequiresFeature';
import { UpgradePlaceholder } from '../components/license/UpgradePlaceholder';
import { PageLoader } from './PageLoader';
import { ErrorLogNavBridge } from './ErrorLogNavBridge';
import { setupApi } from '../api/setup';
import { QUERY_KEYS } from '../api/query-keys';
import { useAuthMode } from '../auth/authMode';
import { useCurrentUser } from '../auth/useCurrentUser';
// Settings sections are small and admin-only — eagerly imported (no separate chunk needed).
import { GeneralSection } from '../features/settings/sections/GeneralSection';
import { MembersSection } from '../features/settings/sections/MembersSection';
import { SearchIndexingSection } from '../features/settings/sections/SearchIndexingSection';
import { ProjectsSection } from '../features/settings/sections/ProjectsSection';
import { DangerZoneSection } from '../features/settings/sections/DangerZoneSection';
import { LicenseSection } from '../features/settings/sections/LicenseSection';
import { EmailNotificationsSection } from '../features/settings/sections/EmailNotificationsSection';
import { OutlierSettingsSection } from '../features/settings/sections/OutlierSettingsSection';

const Setup = lazy(() => import('../features/setup/Setup'));
const Dashboard = lazy(() => import('../features/dashboard/Dashboard'));
const Traces = lazy(() => import('../features/traces/Traces'));
const SessionView = lazy(() => import('../features/traces/session-view/SessionView'));
const AnomalyDashboard = lazy(() => import('../features/anomalies/AnomalyDashboard'));
const Costs = lazy(() => import('../features/costs/Costs'));
const TraceyAI = lazy(() => import('../features/tracey/TraceyAI'));
const Agents = lazy(() => import('../features/agents/Agents'));
const Suites = lazy(() => import('../features/suites/Suites'));
const Evaluators = lazy(() => import('../features/evaluators/Evaluators'));
const Runs = lazy(() => import('../features/runs/Runs'));
const Providers = lazy(() => import('../features/providers/Providers'));
const Proposals = lazy(() => import('../features/proposals/Proposals'));
const SettingsLayout = lazy(() => import('../features/settings/SettingsLayout'));
const Playground = lazy(() => import('../features/playground/Playground'));
const EvaluatorPlayground = lazy(() => import('../features/evaluator-playground/EvaluatorPlayground'));
const Users = lazy(() => import('../features/admin/Users'));
const AccountSecurity = lazy(() => import('../features/account/AccountSecurity'));
const ErrorLog = lazy(() => import('../features/error-log/ErrorLog'));
const AuditLog = lazy(() => import('../features/audit-log/AuditLog'));
const Signup = lazy(() => import('../features/auth/Signup'));
const ForgotPassword = lazy(() => import('../features/auth/ForgotPassword'));
const ResetPassword = lazy(() => import('../features/auth/ResetPassword'));

// The evaluators detail moved from a path param (/evaluators/:id) to a query
// param (/evaluators?id=) for consistency with the other master-detail views.
// This keeps old bookmarks and shared links working.
function EvaluatorDeepLinkRedirect() {
  const { id } = useParams<{ id: string }>();
  return <Navigate to={id ? `/evaluators?id=${encodeURIComponent(id)}` : '/evaluators'} replace />;
}

// Notification emails link to a stable /notifications/<id>. There is no notifications *page* — the
// detail drawer lives in the topbar and opens off `?notification=`, so this lands on the dashboard
// with the drawer open.
function NotificationDeepLinkRedirect() {
  const { id } = useParams<{ id: string }>();
  return <Navigate to={id ? `/dashboard?notification=${encodeURIComponent(id)}` : '/dashboard'} replace />;
}

export function AppRoutes() {
  const { data: setupStatus } = useQuery({
    queryKey: QUERY_KEYS.setupStatus,
    queryFn: setupApi.getStatus,
    staleTime: Infinity,
  });
  const { data: authMode } = useAuthMode();
  const currentUser = useCurrentUser();
  const location = useLocation();

  if (setupStatus === undefined || authMode === undefined) return <PageLoader />;

  // Client-side route gating only — the backend must still enforce admin authorization.
  const isAdmin = authMode.mode === 'local' && currentUser?.role === 'Admin';

  // `resetKeys` on the location: react-router reconciles one boundary instance across route
  // changes, so without it a caught error stays on screen on every subsequent page until a reload.
  const wrap = (el: React.ReactNode) => (
    <ErrorBoundary resetKeys={[location.key]}>
      <Suspense fallback={<PageLoader />}>{el}</Suspense>
    </ErrorBoundary>
  );

  const setupNeeded = !setupStatus.isConfigured || (authMode.mode === 'local' && authMode.setupRequired);

  return (
    <>
    <ErrorLogNavBridge enabled={isAdmin} />
    <LocaleSync />
    <Routes>
      {authMode.mode === 'local' && <Route path="/signup" element={wrap(<Signup />)} />}
      {authMode.mode === 'local' && <Route path="/forgot-password" element={wrap(<ForgotPassword />)} />}
      {authMode.mode === 'local' && <Route path="/reset-password" element={wrap(<ResetPassword />)} />}
      <Route
        path="/setup"
        element={setupNeeded ? wrap(<Setup />) : <Navigate to="/dashboard" replace />}
      />
      {/* The shell's own boundary. `Shell` contains one per chrome region, but `ProjectProvider`
          sits above it and its projects query inherits the global `throwOnError` — without this,
          a failed `GET /api/projects` unmounts the React root and blanks the page. Last resort:
          everything inside has a nearer boundary. */}
      <Route
        path="/"
        element={setupNeeded
          ? <Navigate to="/setup" replace />
          : (
            <ErrorBoundary resetKeys={[location.key]}>
              <ProjectProvider><Shell /></ProjectProvider>
            </ErrorBoundary>
          )}
      >
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard" element={wrap(<Dashboard />)} />
        <Route path="traces" element={wrap(<Traces />)} />
        <Route path="sessions/:sessionId" element={wrap(<SessionView />)} />
        <Route path="anomalies" element={wrap(<AnomalyDashboard />)} />
        <Route path="costs" element={wrap(<Costs />)} />
        <Route path="tracey-ai" element={wrap(<RequiresFeature feature="Tracey"><TraceyAI /></RequiresFeature>)} />
        <Route path="agents" element={wrap(<Agents />)} />
        <Route path="suites" element={wrap(<Suites />)} />
        <Route path="evaluators" element={wrap(<Evaluators />)} />
        {/* Legacy/deep-link path form → canonical ?id= query selection. */}
        <Route path="evaluators/:id" element={<EvaluatorDeepLinkRedirect />} />
        <Route path="runs" element={wrap(<Runs />)} />
        {/* Stable link target for notification emails → the topbar drawer's `?notification=`. */}
        <Route path="notifications/:id" element={<NotificationDeepLinkRedirect />} />
        <Route path="playground" element={wrap(<Playground />)} />
        <Route path="evaluator-playground" element={wrap(<EvaluatorPlayground />)} />
        <Route path="upgrade" element={wrap(<UpgradePlaceholder />)} />
        {/* Per-user account security (MFA) — available to every authenticated user, not admin-gated. */}
        <Route path="account" element={wrap(<AccountSecurity />)} />
        <Route
          path="proposals"
          element={wrap(<RequiresFeature feature="OptimizationProposals"><Proposals /></RequiresFeature>)}
        />
        {/* The entire settings hub is admin-only (the backend independently enforces this on every
            settings-mutating endpoint). Providers, Users, and the Error Log now live here as
            sections rather than as top-level routes. */}
        {isAdmin && (
          <Route path="settings" element={wrap(<SettingsLayout />)}>
            <Route index element={<Navigate to="/settings/general" replace />} />
            <Route path="general" element={<GeneralSection />} />
            <Route path="members" element={<MembersSection />} />
            <Route path="search" element={<SearchIndexingSection />} />
            <Route path="projects" element={<ProjectsSection />} />
            <Route path="providers" element={wrap(<Providers />)} />
            <Route path="users" element={wrap(<Users />)} />
            <Route path="license" element={<LicenseSection />} />
            <Route path="error-log" element={wrap(<ErrorLog />)} />
            <Route path="email-notifications" element={<EmailNotificationsSection />} />
            <Route path="outlier-detection" element={<OutlierSettingsSection />} />
            <Route path="audit-log" element={wrap(<AuditLog />)} />
            <Route path="danger" element={<DangerZoneSection />} />
          </Route>
        )}
        {/* Legacy paths → settings sections, so bookmarks, Tracey, and docs links keep working. */}
        {isAdmin && <Route path="providers" element={<Navigate to="/settings/providers" replace />} />}
        {isAdmin && <Route path="error-log" element={<Navigate to="/settings/error-log" replace />} />}
        {isAdmin && <Route path="admin/users" element={<Navigate to="/settings/users" replace />} />}
        {isAdmin && <Route path="admin/invites" element={<Navigate to="/settings/users" replace />} />}
        {/* Project-scoped audit log — visible to all members (admin global view is /settings/audit-log). */}
        <Route path="audit-log" element={wrap(<AuditLog projectScoped />)} />
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Route>
    </Routes>
    </>
  );
}
