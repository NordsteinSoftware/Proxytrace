import { test, expect } from '../helpers/fixtures';
import { ProxytraceApiClient } from '../helpers/api-client';

// Free-tier feature gates. This spec runs in the `licensing` Playwright project, whose baseURL is
// the unlicensed stack on :5103 (frontend-free → api-free, started WITHOUT PROXYTRACE_LICENSE).
// The default suite runs against the Enterprise `api` on :5101, so the two tiers are exercised in
// the same run by two side-by-side API processes sharing one database.
//
// Only stateless feature gates are asserted here (the OptimizationProposals feature is unlicensed
// regardless of DB contents). Count-based limit gates (e.g. MaxTestSuites) are intentionally NOT
// tested here: the shared DB makes the row count non-deterministic across specs.
const ADMIN_EMAIL = 'admin@e2e.test';
const ADMIN_PASSWORD = 'E2ePassword1!';

test.describe('Free-tier feature gates', () => {
  // Playwright forbids reusing the beforeAll `request` fixture inside a test, so persist only the
  // token and rebuild a client per test against that test's own `request` fixture.
  let token: string;

  test.beforeAll(async ({ request }) => {
    const api = new ProxytraceApiClient(request);
    ({ token } = await api.login(ADMIN_EMAIL, ADMIN_PASSWORD));
  });

  test('the backend reports the Free tier with no Enterprise features', async ({ request }) => {
    const api = new ProxytraceApiClient(request, token);
    const license = await api.getLicense();
    expect(license.tier).toBe('free');
    expect(license.features).not.toContain('OptimizationProposals');
    expect(license.features).not.toContain('Tracey');
    expect(license.features).not.toContain('TestCaseSynthesis');
  });

  test('the optimization-proposals API is gated with HTTP 402', async ({ request }) => {
    const api = new ProxytraceApiClient(request, token);
    const res = await api.proposalsResponse();
    expect(res.status(), 'a Free-tier install must be refused the gated endpoint').toBe(402);
    const body = await res.json();
    expect(body.error?.type).toBe('FeatureNotLicensed');
  });

  test('the Tracey session API is gated with HTTP 402', async ({ request }) => {
    const api = new ProxytraceApiClient(request, token);
    const res = await api.traceySessionResponse();
    expect(res.status(), 'a Free-tier install must be refused the gated endpoint').toBe(402);
    const body = await res.json();
    expect(body.error?.type).toBe('FeatureNotLicensed');
  });

  test('the test-case synthesis API is gated with HTTP 402', async ({ request }) => {
    const api = new ProxytraceApiClient(request, token);
    // The feature gate runs before the action, so an id that resolves to nothing still 402s —
    // which is the point: an unlicensed install never reaches the model call.
    const res = await api.testCaseProposalsResponse('00000000-0000-0000-0000-000000000001');
    expect(res.status(), 'a Free-tier install must be refused the gated endpoint').toBe(402);
    const body = await res.json();
    expect(body.error?.type).toBe('FeatureNotLicensed');
  });

  test('the license badge shows Free and links to the upgrade page', async ({ page }) => {
    await page.goto('/dashboard', { waitUntil: 'load' });

    const badge = page.getByTestId('license-badge');
    await expect(badge).toBeVisible();
    await expect(badge).toContainText('Free');

    await badge.click();
    await expect(page).toHaveURL(/\/upgrade$/);
    await expect(page.getByTestId('upgrade-placeholder')).toBeVisible();
  });

  test('a feature-gated route renders the upgrade placeholder, not the feature', async ({ page }) => {
    await page.goto('/proposals', { waitUntil: 'load' });

    // RequiresFeature gates the route client-side from the license snapshot, so the Proposals
    // feature never mounts; the upgrade placeholder takes its place.
    await expect(page.getByTestId('upgrade-placeholder')).toBeVisible();
    await expect(page.getByTestId('upgrade-cta')).toBeVisible();
  });

  test('the Tracey AI route renders the upgrade placeholder, not the feature', async ({ page }) => {
    await page.goto('/tracey-ai', { waitUntil: 'load' });

    // RequiresFeature gates the route client-side from the license snapshot, so Tracey never
    // mounts; the upgrade placeholder takes its place.
    await expect(page.getByTestId('upgrade-placeholder')).toBeVisible();
    await expect(page.getByTestId('upgrade-cta')).toBeVisible();
  });
});
