// Runs INSIDE the frontend's Vite module graph (served as `virtual:prompt-lab`), which is what
// lets it import Tracey's real prompt, her real Zod tool schemas, and her `?raw`-inlined skill
// markdown exactly as the browser does. Never import this file directly from Node — the `/src/…`
// specifiers and the `.ts` sources only resolve through Vite's SSR loader (see tracey-driver.mjs).
import { generateText, stepCountIs, tool as aiTool, zodSchema } from 'ai';
import { createOpenAI } from '@ai-sdk/openai';
import { TRACEY_SYSTEM_PROMPT } from '/src/features/tracey/tracey-prompt.ts';
import { createTraceyTools } from '/src/features/tracey/tracey-tools.ts';
import { activeToolNamesFor } from '/src/features/tracey/tool-access.ts';

/**
 * Tools whose real `execute` runs client-side only — no API, no browser storage — so the lab runs
 * them for real instead of stubbing them. `load_skill` matters most: it returns the actual playbook
 * and unlocks that skill's tool bundle, so progressive disclosure behaves exactly as in the app.
 */
const NATIVE_TOOLS = new Set(['navigate', 'load_skill', 'search_docs']);

/**
 * The inline renderers. Their real `execute` stashes the payload in the browser artifact store and
 * hands the model back only `{ kind, title }` — reproduced here rather than run, because outside a
 * browser the store's fallback path echoes the whole payload back into the model's context, which
 * is precisely the context bloat the artifact store exists to prevent.
 */
const DISPLAY_TOOL_KINDS = { show_chart: 'chart', show_table: 'table', show_text: 'text' };

export function getSystemPrompt() {
  return TRACEY_SYSTEM_PROMPT;
}

/** Everything the model returned for one step, flattened to plain JSON the runner can serialize. */
function serializeStep(step) {
  const calls = (step.toolCalls ?? []).map((call) => ({
    name: call.toolName,
    args: call.input ?? call.args ?? {},
  }));
  const results = (step.toolResults ?? []).map((result) => ({
    name: result.toolName,
    result: result.output ?? result.result,
  }));
  return { text: step.text ?? '', toolCalls: calls, toolResults: results };
}

/**
 * Drive one scenario (a list of user turns) against the live upstream model with Tracey's real
 * prompt and tools. Mirrors `TraceyTransport`: same `.chat()` (Chat Completions) model, same system
 * prompt, same per-step `activeTools` disclosure, same loop-until-answered `stopWhen`.
 */
export async function runScenario(cfg) {
  const loadedSkillIds = new Set();
  const ctx = {
    projectId: '00000000-0000-0000-0000-000000000001',
    artifactScope: 'prompt-lab:lab',
    navigate: () => {},
    confirm: async () => true,
    loadedSkillIds,
  };

  const fixtures = cfg.fixtures ?? {};
  const unfixtured = new Set();
  const definitions = createTraceyTools(ctx);
  const tools = {};

  for (const [name, def] of Object.entries(definitions)) {
    const shared = { description: def.description, inputSchema: zodSchema(def.parameters) };
    // A tool with no `execute` is human-in-the-loop in the app (ask_questions): the SDK emits the
    // call and the turn pauses for the user. Leaving it execute-less here reproduces that — the
    // turn ends on the question, which is the behavior worth seeing in the transcript.
    if (!def.execute) {
      tools[name] = aiTool(shared);
      continue;
    }
    tools[name] = aiTool({
      ...shared,
      execute: async (args) => {
        if (NATIVE_TOOLS.has(name)) return def.execute(args, ctx);
        if (Object.hasOwn(DISPLAY_TOOL_KINDS, name)) {
          return { kind: DISPLAY_TOOL_KINDS[name], title: args.title };
        }
        if (Object.hasOwn(fixtures, name)) return fixtures[name];
        unfixtured.add(name);
        // Answer plausibly rather than erroring: a thrown tool result would derail the turn and
        // hide the prompt behavior under test. The runner flags the gap in the transcript so the
        // reader knows this branch ran on a stand-in, not on the fixture they expected.
        return {
          promptLab: `No fixture defined for "${name}" — treat this as: the call succeeded, with nothing further to report.`,
          ok: true,
          count: 0,
          items: [],
        };
      },
    });
  }

  const openai = createOpenAI({ baseURL: cfg.llm.baseURL, apiKey: cfg.llm.apiKey });
  const model = openai.chat(cfg.llm.model);

  const messages = [];
  const turns = [];

  for (const userText of cfg.turns) {
    messages.push({ role: 'user', content: userText });
    const startedAt = Date.now();
    try {
      const result = await generateText({
        model,
        system: TRACEY_SYSTEM_PROMPT,
        messages,
        tools,
        ...(cfg.temperature === null ? {} : { temperature: cfg.temperature }),
        // Progressive disclosure, straight from the app: CORE plus the bundles of every skill
        // loaded so far. `load_skill` runs natively above, so this set widens mid-turn for real.
        prepareStep: () => ({ activeTools: activeToolNamesFor([...loadedSkillIds]) }),
        stopWhen: stepCountIs(cfg.maxSteps ?? 12),
      });
      messages.push(...result.response.messages);
      turns.push({
        user: userText,
        steps: result.steps.map(serializeStep),
        text: result.text ?? '',
        finishReason: result.finishReason,
        usage: {
          inputTokens: result.usage?.inputTokens ?? 0,
          outputTokens: result.usage?.outputTokens ?? 0,
        },
        durationMs: Date.now() - startedAt,
      });
    } catch (error) {
      turns.push({
        user: userText,
        steps: [],
        text: '',
        error: String(error?.message ?? error),
        durationMs: Date.now() - startedAt,
      });
      break;
    }
  }

  return { turns, unfixtured: [...unfixtured], loadedSkills: [...loadedSkillIds] };
}
