import { defineConfig, devices } from '@playwright/test';
import { config } from 'dotenv';
import { resolve } from 'path';

// Load e2e/.env (if present) before Playwright reads process.env.
// Shell env vars take precedence over .env values.
config({ path: resolve(__dirname, '.env'), override: false });

// The whole suite runs against a single shared database, and several specs assume an ordering
// (e.g. the Agents empty-state must observe a tenant with no agents before any agent is seeded).
// Force a single worker so projects/files execute serially and deterministically.
const STORAGE_STATE = '.auth/storageState.json';
const CHROME = devices['Desktop Chrome'];
// @llm projects hit a real provider; retry to absorb transient upstream latency/throttling
// without masking deterministic failures in the non-LLM projects (which keep retries: 0 locally).
const LLM_RETRIES = process.env.CI ? 2 : 1;

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI
    ? [['html', { outputFolder: 'playwright-report', open: 'never' }], ['github']]
    : [['html', { outputFolder: 'playwright-report', open: 'on-failure' }]],
  use: {
    baseURL: 'http://localhost:5101',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'setup',
      testMatch: /auth\.setup\.spec\.ts/,
    },
    {
      // Every non-LLM spec that runs authenticated against the default (Enterprise) stack. The
      // browser session is restored from the setup project's storageState.
      name: 'core',
      testMatch: [
        '**/core-crud.spec.ts',
        '**/agents.spec.ts',
        '**/providers.spec.ts',
        '**/suites.spec.ts',
        '**/traces.spec.ts',
        '**/traces-filters.spec.ts',
        '**/traces-grouping.spec.ts',
        '**/sessions.spec.ts',
        '**/outliers.spec.ts',
        '**/evaluators.spec.ts',
        '**/dashboard.spec.ts',
        '**/settings.spec.ts',
        '**/admin.spec.ts',
        '**/error-handling.spec.ts',
        '**/error-log.spec.ts',
        '**/audit-log.spec.ts',
        '**/proposals.spec.ts',
        '**/proposals-kinds.spec.ts',
        '**/schedules.spec.ts',
        '**/theories.spec.ts',
        '**/cancel.spec.ts',
        '**/cost.spec.ts',
        '**/search.spec.ts',
        '**/tenancy.spec.ts',
        '**/delete-cascade.spec.ts',
        '**/pagination.spec.ts',
        '**/negative.spec.ts',
        '**/sse.spec.ts',
        '**/mfa.spec.ts',
        '**/notifications.spec.ts',
      ],
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'smoke',
      testMatch: /smoke\.spec\.ts/,
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['setup'],
    },
    {
      // Auth & access-control flows test login/logout/signup from a clean session, so this project
      // intentionally loads NO storageState — each spec drives auth itself.
      name: 'auth-flows',
      testMatch: /auth-flows\.spec\.ts/,
      use: { ...CHROME },
      dependencies: ['setup'],
    },
    {
      name: 'llm-ingestion',
      testMatch: /ingestion\.spec\.ts/,
      // @llm specs make real upstream round-trips; a transient provider latency/throttle blip
      // would otherwise fail a deterministic assertion outright. Retry to absorb that noise.
      retries: LLM_RETRIES,
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['setup'],
    },
    {
      // proxy-trace re-verifies the proxy → Traces UI path; depends only on a completed setup.
      name: 'llm-proxy-trace',
      testMatch: /proxy-trace\.spec\.ts/,
      retries: LLM_RETRIES,
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['setup'],
    },
    {
      // test-run relies on an agent, which only exists once a call has been ingested. Depend on
      // the ingestion project so it always runs first, even with multiple workers.
      name: 'llm-test-run',
      testMatch: /test-run\.spec\.ts/,
      retries: LLM_RETRIES,
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['llm-ingestion'],
    },
    {
      // Tracey chat specs (await flow, skill persistence, inline cards); each self-seeds its
      // agent + suite, so they depend only on a completed setup.
      name: 'llm-tracey',
      testMatch: /tracey-(await|skills)\.spec\.ts/,
      retries: LLM_RETRIES,
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['setup'],
    },
    {
      // Playground drives a live agent against an endpoint; needs an ingested agent present.
      name: 'llm-playground',
      testMatch: ['**/playground.spec.ts'],
      retries: LLM_RETRIES,
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['llm-ingestion'],
    },
    {
      // Evaluator test-bench: the agentic bench needs an ingested agent; the rule-based bench in
      // the same file self-seeds and needs no LLM.
      name: 'llm-evaluator-playground',
      testMatch: ['**/evaluator-playground.spec.ts'],
      retries: LLM_RETRIES,
      use: { ...CHROME, storageState: STORAGE_STATE },
      dependencies: ['llm-ingestion'],
    },
    {
      // Logs into the Free-tier stack (frontend-free / api-free on :5103) and saves a
      // storageState for that origin. The admin already exists (shared DB, created by `setup`),
      // so this only authenticates the browser against the :5103 origin.
      name: 'licensing-setup',
      testMatch: /licensing\.setup\.spec\.ts/,
      use: { ...CHROME, baseURL: 'http://localhost:5103' },
      dependencies: ['setup'],
    },
    {
      // Free-tier feature-gate checks: must hit the unlicensed :5103 stack, not the Enterprise
      // default (:5101). Non-LLM and stateless (feature gates 402 regardless of DB contents),
      // so it needs only `licensing-setup`, not the ingestion projects.
      name: 'licensing',
      testMatch: /licensing\.spec\.ts/,
      use: { ...CHROME, baseURL: 'http://localhost:5103', storageState: '.auth/licensing-state.json' },
      dependencies: ['licensing-setup'],
    },
  ],
});
