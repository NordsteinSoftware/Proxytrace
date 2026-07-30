import { test, expect } from '../helpers/fixtures';

// Every main route must load authenticated (storageState from the setup project), render the
// nav chrome, and produce zero JS console / page errors. Adding a route here is the cheapest
// regression guard against a page that white-screens on mount.
const ROUTES = [
  { path: '/', label: 'root' },
  { path: '/dashboard', label: 'dashboard' },
  { path: '/traces', label: 'traces' },
  { path: '/anomalies', label: 'anomalies' },
  { path: '/costs', label: 'costs' },
  { path: '/agents', label: 'agents' },
  { path: '/suites', label: 'suites' },
  { path: '/runs', label: 'runs' },
  { path: '/evaluators', label: 'evaluators' },
  { path: '/playground', label: 'playground' },
  { path: '/evaluator-playground', label: 'evaluator-playground' },
  { path: '/proposals', label: 'proposals' },
  { path: '/account', label: 'account security' },
  // Settings is an admin-only hub; smoke runs as admin (storageState). Providers, Users, and the
  // Error Log now live as sections under /settings. `/settings` itself redirects to general.
  { path: '/settings', label: 'settings (→ general)' },
  { path: '/settings/general', label: 'settings general' },
  { path: '/settings/members', label: 'settings members' },
  { path: '/settings/search', label: 'settings search' },
  { path: '/settings/projects', label: 'settings projects' },
  { path: '/settings/providers', label: 'settings providers' },
  { path: '/settings/users', label: 'settings users' },
  { path: '/settings/license', label: 'settings license' },
  { path: '/settings/error-log', label: 'settings error log' },
  { path: '/settings/audit-log', label: 'settings audit log' },
  { path: '/settings/danger', label: 'settings danger zone' },
  { path: '/audit-log', label: 'audit log (project-scoped)' },
];

for (const { path, label } of ROUTES) {
  test(`${label} loads without JS console errors`, async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') errors.push(msg.text());
    });
    page.on('pageerror', (err) => errors.push(err.message));

    await page.goto(path, { waitUntil: 'load' });
    await expect(page).not.toHaveURL(/\/login/);
    await expect(page.getByRole('navigation')).toBeVisible();
    expect(errors, `console errors on ${path}: ${errors.join('; ')}`).toHaveLength(0);
  });
}

test('unknown path redirects to the dashboard', async ({ page }) => {
  await page.goto('/does-not-exist', { waitUntil: 'load' });
  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('navigation')).toBeVisible();
});

// The password-reset pages render outside the authenticated app shell (no nav chrome), so they
// can't ride the ROUTES loop above. Smoke them directly: each must mount its form/state cleanly with
// zero console errors. The smoke session is authenticated, but LocalAuthGate lets these paths through.
test('forgot-password page renders without JS console errors', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', (msg) => { if (msg.type() === 'error') errors.push(msg.text()); });
  page.on('pageerror', (err) => errors.push(err.message));

  await page.goto('/forgot-password', { waitUntil: 'load' });
  await expect(page.getByTestId('forgot-password-form')).toBeVisible();
  expect(errors, `console errors on /forgot-password: ${errors.join('; ')}`).toHaveLength(0);
});

test('reset-password page renders its invalid-link state without JS console errors', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', (msg) => { if (msg.type() === 'error') errors.push(msg.text()); });
  page.on('pageerror', (err) => errors.push(err.message));

  // No token in the URL → the page shows the invalid-link state (still a clean mount).
  await page.goto('/reset-password', { waitUntil: 'load' });
  await expect(page.getByTestId('reset-password-invalid')).toBeVisible();
  expect(errors, `console errors on /reset-password: ${errors.join('; ')}`).toHaveLength(0);
});
