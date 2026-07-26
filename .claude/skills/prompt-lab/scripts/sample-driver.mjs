// Runs a sample-client agent (support / travel / code / data) against the live upstream model.
// Unlike Tracey, these agents need no fixtures: `chat.js` ships a deterministic tool simulator and
// the same `runChat` loop the Express server and the playlist runner use, so the lab exercises the
// real turn-1/turn-2 tool-calling flow rather than an imitation of it.
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

async function loadOpenAI(clientDir) {
  const mod = await import(pathToFileURL(join(clientDir, 'node_modules/openai/index.js')).href);
  return mod.default?.default ?? mod.default ?? mod.OpenAI;
}

/** Open the sample-client agent catalogue from a checkout. Handle mirrors the Tracey lab's shape. */
export async function openSampleLab(checkoutRoot) {
  const clientDir = join(checkoutRoot, 'sample-client');
  const chat = await import(pathToFileURL(join(clientDir, 'chat.js')).href);
  const OpenAI = await loadOpenAI(clientDir);
  const agents = chat.loadAgents();

  return {
    agentIds: () => Object.keys(agents),
    systemPrompt: (agentId) => agents[agentId]?.systemPrompt ?? '',
    close: async () => {},

    async run(cfg) {
      const agent = agents[cfg.agentId];
      if (!agent) {
        throw new Error(`Unknown sample-client agent "${cfg.agentId}". Available: ${Object.keys(agents).join(', ')}`);
      }
      const openai = new OpenAI({ apiKey: cfg.llm.apiKey, baseURL: cfg.llm.baseURL });
      const params = {
        ...(agent.defaultParams ?? {}),
        ...(cfg.temperature === null ? {} : { temperature: cfg.temperature }),
      };

      const history = [];
      const turns = [];

      for (const userText of cfg.turns) {
        const startedAt = Date.now();
        // `runChat` emits text as deltas and one event per tool call/result. Fold them into the
        // same {steps, text} shape the Tracey driver returns so the report renders both agents
        // identically and the reader compares like with like.
        const steps = [];
        let pending = { text: '', toolCalls: [], toolResults: [] };
        const flush = () => {
          if (pending.text || pending.toolCalls.length) steps.push(pending);
          pending = { text: '', toolCalls: [], toolResults: [] };
        };

        try {
          await chat.runChat({
            agent,
            messages: [...history, { role: 'user', content: userText }],
            openai,
            model: cfg.llm.model,
            params,
            onEvent: (event) => {
              if (event.text !== undefined) pending.text += event.text;
              if (event.toolCall) {
                pending.toolCalls.push({
                  name: event.toolCall.name,
                  args: safeParse(event.toolCall.arguments),
                });
              }
              if (event.toolResult) {
                pending.toolResults.push({
                  name: event.toolResult.name,
                  result: safeParse(event.toolResult.result),
                });
                flush();
              }
            },
          });
          flush();
          const text = steps.map((step) => step.text).join('').trim();
          history.push({ role: 'user', content: userText }, { role: 'assistant', content: text });
          turns.push({ user: userText, steps, text, durationMs: Date.now() - startedAt });
        } catch (error) {
          turns.push({
            user: userText,
            steps,
            text: '',
            error: String(error?.message ?? error),
            durationMs: Date.now() - startedAt,
          });
          break;
        }
      }

      return { turns, unfixtured: [], loadedSkills: [] };
    },
  };
}

function safeParse(value) {
  if (typeof value !== 'string') return value;
  try {
    return JSON.parse(value);
  } catch {
    return value;
  }
}
