// Boots a headless Vite server rooted at a checkout's `frontend/` and runs Tracey scenarios inside
// its module graph. Going through Vite (rather than importing the .ts by hand) is what makes the
// lab faithful: her prompt, her Zod schemas, and her `?raw` skill markdown load exactly as they do
// in the browser, so there is no second copy of any of them to drift.
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { fixtureResult } from './fixture-world.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const VIRTUAL_ID = 'virtual:prompt-lab';
const RESOLVED_ID = '\0virtual:prompt-lab';

async function importVite(frontendDir) {
  const require = createRequire(join(frontendDir, 'package.json'));
  try {
    return await import(pathToFileURL(require.resolve('vite')).href);
  } catch {
    return import(pathToFileURL(join(frontendDir, 'node_modules/vite/dist/node/index.js')).href);
  }
}

/**
 * Open a Vite server against `<checkoutRoot>/frontend`. Returned handle exposes `run(scenario)` and
 * must be closed by the caller. One server serves every scenario of a variant — startup dominates
 * the cost, so reusing it keeps a multi-scenario run fast.
 */
export async function openTraceyLab(checkoutRoot) {
  const frontendDir = join(checkoutRoot, 'frontend');
  const moduleSource = readFileSync(join(HERE, 'tracey-lab-module.js'), 'utf8');
  const { createServer } = await importVite(frontendDir);

  // The frontend's Vite config loads the Lingui plugin, which looks for lingui.config.ts relative to
  // the *process* cwd, not the Vite root — so the lab has to stand in the frontend directory for as
  // long as the server lives. Restored on close.
  const previousCwd = process.cwd();
  process.chdir(frontendDir);

  const server = await createServer({
    root: frontendDir,
    configFile: join(frontendDir, 'vite.config.ts'),
    appType: 'custom',
    logLevel: 'error',
    server: { middlewareMode: true, hmr: false, watch: null },
    plugins: [
      {
        name: 'prompt-lab-virtual',
        resolveId: (id) => (id === VIRTUAL_ID ? RESOLVED_ID : null),
        load: (id) => (id === RESOLVED_ID ? moduleSource : null),
      },
    ],
  });

  const mod = await server.ssrLoadModule(VIRTUAL_ID);

  return {
    systemPrompt: () => mod.getSystemPrompt(),
    // The fixture resolver is injected rather than imported by the lab module: that module is served
    // as a virtual Vite module, so a relative import from it has nothing to resolve against. Passing
    // it in also keeps the DSL in plain Node, where it is directly testable (fixture-world.test.mjs).
    run: (cfg) => mod.runScenario({ ...cfg, fixtureResult }),
    close: async () => {
      await server.close();
      process.chdir(previousCwd);
    },
  };
}
