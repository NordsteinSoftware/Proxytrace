#!/usr/bin/env node
// prompt-lab — fire scenarios at the real upstream model with an agent's real system prompt and
// tools, then write a transcript report for a human (or Claude) to read.
//
// The A/B is the point: unless told otherwise it runs every scenario twice — once against the
// prompt as committed (a detached worktree at HEAD), once against your working copy — because
// "did my edit change the behavior?" is only answerable next to what the behavior was.
//
//   node run.mjs --agent tracey
//   node run.mjs --agent tracey --only brevity,skill-dispatch --repeat 3
//   node run.mjs --agent support --baseline none
//
// Credentials come from the repo-root .env (KIOSK_LLM_BASE_URL / _API_KEY / _MODEL) — the same
// endpoint the kiosk stack talks to, so the model under test is the model you demo on.
import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const SKILL_DIR = resolve(HERE, '..');

const SAMPLE_AGENTS = ['support', 'travel', 'code', 'data'];
// Files whose contents shape an agent's behavior. Used to decide whether an A/B is worth running
// and to tell the reader exactly what differs between the two variants.
const WATCHED_PATHS = {
  tracey: ['frontend/src/features/tracey'],
  sample: ['sample-client/chat.js', 'sample-client/examples'],
};

// ── args & environment ───────────────────────────────────────────────────────

function parseArgs(argv) {
  const args = {
    agent: 'tracey',
    scenarios: null,
    fixtures: null,
    only: null,
    baseline: 'HEAD',
    repeat: 1,
    temperature: 0,
    maxSteps: 12,
    model: null,
    out: null,
  };
  for (let i = 0; i < argv.length; i++) {
    const flag = argv[i];
    if (flag === '--help' || flag === '-h') {
      usage();
      process.exit(0);
    }
    const value = argv[++i];
    if (value === undefined) throw new Error(`${flag} needs a value`);
    switch (flag) {
      case '--agent': args.agent = value; break;
      case '--scenarios': args.scenarios = value; break;
      case '--fixtures': args.fixtures = value; break;
      case '--only': args.only = value.split(',').map((s) => s.trim()).filter(Boolean); break;
      case '--baseline': args.baseline = value; break;
      case '--repeat': args.repeat = Number(value); break;
      case '--temperature': args.temperature = value === 'default' ? null : Number(value); break;
      case '--max-steps': args.maxSteps = Number(value); break;
      case '--model': args.model = value; break;
      case '--out': args.out = value; break;
      default: throw new Error(`Unknown flag ${flag}`);
    }
  }
  if (!Number.isInteger(args.repeat) || args.repeat < 1) throw new Error('--repeat must be a positive integer');
  return args;
}

function usage() {
  console.log(`prompt-lab — run an agent's real prompt against the live model

  --agent <id>        tracey (default) | ${SAMPLE_AGENTS.join(' | ')}
  --scenarios <path>  scenario JSON (default: scenarios/<tracey|sample-client>.json)
  --fixtures <path>   fixture JSON for Tracey's data tools (default: fixtures/tracey.json)
  --only a,b          run only these scenario ids
  --baseline <ref>    git ref to A/B against, or "none" for a single run (default: HEAD)
  --repeat <n>        runs per scenario per variant (default: 1; use 2-3 for close calls)
  --temperature <t>   sampling temperature, or "default" to leave it unset (default: 0)
  --max-steps <n>     tool-loop cap per turn (default: 12)
  --model <id>        override KIOSK_LLM_MODEL
  --out <dir>         report directory (default: <repo>/.prompt-lab/<timestamp>)`);
}

function loadDotEnv(path) {
  const env = {};
  if (!existsSync(path)) return env;
  for (const line of readFileSync(path, 'utf8').split('\n')) {
    const match = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(line);
    if (!match || line.trim().startsWith('#')) continue;
    env[match[1]] = match[2].trim().replace(/^["']|["']$/g, '');
  }
  return env;
}

const git = (repoRoot, ...gitArgs) =>
  execFileSync('git', gitArgs, { cwd: repoRoot, encoding: 'utf8' }).trim();

// ── variants ─────────────────────────────────────────────────────────────────

/**
 * Files under the agent's watched paths that differ between the working tree and `ref` — including
 * untracked ones, since a brand-new skill markdown file is exactly the kind of change worth A/Bing.
 */
function changedFiles(repoRoot, ref, paths) {
  const tracked = git(repoRoot, 'diff', '--name-only', ref, '--', ...paths);
  const untracked = git(repoRoot, 'ls-files', '--others', '--exclude-standard', '--', ...paths);
  return [...tracked.split('\n'), ...untracked.split('\n')].map((s) => s.trim()).filter(Boolean);
}

/**
 * Check out `ref` into a throwaway worktree so the baseline runs against committed sources without
 * touching the working tree (no stashing, no risk of losing edits). node_modules and gitignored
 * build artifacts are shared from the working tree — the worktree only needs to differ in source.
 */
function createBaselineCheckout(repoRoot, ref) {
  const dir = mkdtempSync(join(tmpdir(), 'prompt-lab-'));
  const path = join(dir, 'checkout');
  git(repoRoot, 'worktree', 'add', '--detach', '--quiet', path, ref);

  for (const pkg of ['frontend', 'sample-client']) {
    const source = join(repoRoot, pkg, 'node_modules');
    if (existsSync(source) && existsSync(join(path, pkg))) {
      symlinkSync(source, join(path, pkg, 'node_modules'), 'dir');
    }
  }
  // Gitignored but required: Tracey's manual index is generated by `npm run gen:docs`, so a fresh
  // worktree has none and `search_docs` would fail to import.
  const docsIndex = 'frontend/src/features/tracey/knowledge/docs-index.generated.ts';
  if (existsSync(join(repoRoot, docsIndex))) {
    cpSync(join(repoRoot, docsIndex), join(path, docsIndex));
  }

  return {
    path,
    cleanup: () => {
      try {
        git(repoRoot, 'worktree', 'remove', '--force', path);
      } catch {
        /* best effort — the temp dir goes away below regardless */
      }
      rmSync(dir, { recursive: true, force: true });
    },
  };
}

// ── report rendering ─────────────────────────────────────────────────────────

const words = (text) => (text.trim() ? text.trim().split(/\s+/).length : 0);
const clip = (value, max = 600) => {
  const text = typeof value === 'string' ? value : JSON.stringify(value);
  if (text === undefined) return 'undefined';
  return text.length > max ? `${text.slice(0, max)}… (${text.length} chars)` : text;
};

/** Mechanical, judgement-free facts about a turn. The reader draws the conclusions. */
function observe(turn) {
  const toolNames = turn.steps.flatMap((step) => step.toolCalls.map((call) => call.name));
  const interstitial = turn.steps
    .filter((step) => step.toolCalls.length > 0 && step.text.trim())
    .map((step) => step.text.trim());
  return {
    steps: turn.steps.length,
    toolCalls: toolNames,
    finalWords: words(turn.text),
    interstitialText: interstitial,
    durationMs: turn.durationMs,
  };
}

function renderRun(run) {
  const lines = [];
  for (const turn of run.result.turns) {
    lines.push(`**User:** ${turn.user}`, '');
    for (const step of turn.steps) {
      if (step.text.trim() && step.toolCalls.length > 0) {
        lines.push(`- 💬 *text emitted mid-tool-loop:* ${clip(step.text.trim(), 300)}`);
      }
      for (const call of step.toolCalls) {
        const result = step.toolResults.find((r) => r.name === call.name);
        lines.push(`- 🔧 \`${call.name}(${clip(call.args, 200)})\``);
        lines.push(`  → ${clip(result ? result.result : '(no result — turn ended here)', 300)}`);
      }
    }
    if (turn.error) {
      lines.push('', `> ⛔ **error:** ${turn.error}`);
    } else {
      const observed = observe(turn);
      lines.push('', '**Assistant:**', '~~~text', turn.text || '(no text)', '~~~');
      lines.push(
        `*${observed.steps} step(s) · ${observed.toolCalls.length} tool call(s) · ` +
        `${observed.finalWords} words · ${(turn.durationMs / 1000).toFixed(1)}s*`,
      );
    }
    lines.push('');
  }
  if (run.result.unfixtured.length) {
    lines.push(`> ⚠️ no fixture for: ${run.result.unfixtured.join(', ')} — these ran against an empty world.`, '');
  }
  return lines.join('\n');
}

function renderReport(meta, scenarios, runs) {
  const lines = [
    `# prompt-lab — ${meta.agent}`,
    '',
    `- **model:** \`${meta.model}\` via ${meta.baseHost}`,
    `- **temperature:** ${meta.temperature === null ? 'provider default' : meta.temperature}`,
    `- **variants:** ${meta.variants.join(' · ')}`,
    `- **runs per scenario:** ${meta.repeat}`,
    `- **when:** ${meta.startedAt}`,
    '',
  ];

  if (meta.changed.length) {
    lines.push(`Working copy differs from \`${meta.baselineRef}\` in:`, '');
    for (const file of meta.changed) lines.push(`- \`${file}\``);
    lines.push('');
  } else if (meta.variants.length === 1 && meta.baselineRef) {
    lines.push(
      `> No differences from \`${meta.baselineRef}\` under ${WATCHED_PATHS[meta.watched].join(', ')} —` +
      ' ran a single variant. Commit or edit the prompt first if you wanted an A/B.',
      '',
    );
  }

  lines.push('## At a glance', '', '| scenario | variant | run | steps | tools | words |', '|---|---|---|---|---|---|');
  for (const run of runs) {
    const turn = run.result.turns.at(-1);
    const observed = turn ? observe(turn) : { steps: 0, toolCalls: [], finalWords: 0 };
    lines.push(
      `| ${run.scenario} | ${run.variant} | ${run.index} | ${observed.steps} | ` +
      `${observed.toolCalls.join(', ') || '—'} | ${observed.finalWords} |`,
    );
  }
  lines.push('');

  for (const scenario of scenarios) {
    lines.push(`## ${scenario.id}`, '');
    if (scenario.intent) lines.push(`*What this probes:* ${scenario.intent}`, '');
    for (const run of runs.filter((r) => r.scenario === scenario.id)) {
      lines.push(`### ${run.variant}${meta.repeat > 1 ? ` — run ${run.index}` : ''}`, '', renderRun(run));
    }
  }

  return lines.join('\n');
}

// ── main ─────────────────────────────────────────────────────────────────────

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const repoRoot = git(process.cwd(), 'rev-parse', '--show-toplevel');

  const isTracey = args.agent === 'tracey';
  if (!isTracey && !SAMPLE_AGENTS.includes(args.agent)) {
    throw new Error(`Unknown --agent "${args.agent}". Use tracey or one of: ${SAMPLE_AGENTS.join(', ')}`);
  }
  const watched = isTracey ? 'tracey' : 'sample';

  const env = { ...loadDotEnv(join(repoRoot, '.env')), ...process.env };
  const llm = {
    baseURL: env.KIOSK_LLM_BASE_URL,
    apiKey: env.KIOSK_LLM_API_KEY,
    model: args.model ?? env.KIOSK_LLM_MODEL,
  };
  if (!llm.baseURL || !llm.apiKey || !llm.model) {
    throw new Error(
      'Missing KIOSK_LLM_BASE_URL / KIOSK_LLM_API_KEY / KIOSK_LLM_MODEL. Copy kiosk.env.example to ' +
      '.env at the repo root and fill them in — the lab talks to the same endpoint the kiosk uses.',
    );
  }

  const scenarioPath = args.scenarios
    ?? join(SKILL_DIR, 'scenarios', isTracey ? 'tracey.json' : 'sample-client.json');
  const loaded = JSON.parse(readFileSync(scenarioPath, 'utf8'));
  const all = (loaded.scenarios ?? loaded).filter((s) => !s.agent || s.agent === args.agent);
  const scenarios = args.only ? all.filter((s) => args.only.includes(s.id)) : all;
  if (!scenarios.length) throw new Error(`No scenarios matched in ${scenarioPath}`);

  const fixtures = isTracey
    ? JSON.parse(readFileSync(args.fixtures ?? join(SKILL_DIR, 'fixtures', 'tracey.json'), 'utf8'))
    : {};

  const changed = args.baseline === 'none' ? [] : changedFiles(repoRoot, args.baseline, WATCHED_PATHS[watched]);
  const useBaseline = args.baseline !== 'none' && changed.length > 0;

  const variants = [{ name: 'working copy', root: repoRoot }];
  let baseline = null;
  if (useBaseline) {
    baseline = createBaselineCheckout(repoRoot, args.baseline);
    variants.unshift({ name: `baseline @ ${args.baseline}`, root: baseline.path });
  }

  const startedAt = new Date().toISOString();
  const outDir = args.out ?? join(repoRoot, '.prompt-lab', startedAt.replace(/[:.]/g, '-'));
  mkdirSync(outDir, { recursive: true });

  const runs = [];
  try {
    for (const variant of variants) {
      // Variants run sequentially on purpose: they share node_modules (and therefore Vite's dep
      // cache), and serializing also keeps upstream rate limits out of the picture.
      const lab = isTracey
        ? await (await import('./tracey-driver.mjs')).openTraceyLab(variant.root)
        : await (await import('./sample-driver.mjs')).openSampleLab(variant.root);
      try {
        for (const scenario of scenarios) {
          for (let index = 1; index <= args.repeat; index++) {
            process.stderr.write(`▶ ${variant.name} · ${scenario.id} · run ${index}\n`);
            const result = await lab.run({
              agentId: args.agent,
              turns: scenario.turns,
              fixtures: { ...fixtures, ...(scenario.fixtures ?? {}) },
              llm,
              temperature: args.temperature,
              maxSteps: args.maxSteps,
            });
            runs.push({ scenario: scenario.id, variant: variant.name, index, result });
          }
        }
      } finally {
        await lab.close();
      }
    }
  } finally {
    baseline?.cleanup();
  }

  const meta = {
    agent: args.agent,
    model: llm.model,
    baseHost: new URL(llm.baseURL).host,
    temperature: args.temperature,
    variants: variants.map((v) => v.name),
    repeat: args.repeat,
    startedAt,
    changed,
    baselineRef: args.baseline === 'none' ? null : args.baseline,
    watched,
  };

  const reportPath = join(outDir, 'report.md');
  writeFileSync(reportPath, renderReport(meta, scenarios, runs));
  writeFileSync(join(outDir, 'raw.json'), JSON.stringify({ meta, scenarios, runs }, null, 2));

  const errors = runs.filter((r) => r.result.turns.some((t) => t.error)).length;
  process.stderr.write(`\n${runs.length} run(s)${errors ? `, ${errors} with errors` : ''}\n`);
  console.log(reportPath);
}

main().catch((error) => {
  console.error(`prompt-lab: ${error.message}`);
  process.exit(1);
});
