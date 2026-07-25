import {
  streamText,
  generateText,
  convertToModelMessages,
  stepCountIs,
  type ChatTransport,
  type UIMessage,
  type UIMessageChunk,
  type ToolSet,
} from 'ai';
import { createOpenAI } from '@ai-sdk/openai';
import { getAccessToken } from '../../auth/token';
import { type TraceyToolContext } from './tracey-tools';
import { activeToolNamesFor } from './tool-access';
import {
  FOLLOW_UP_SYSTEM_PROMPT,
  buildFollowUpPrompt,
  parseFollowUps,
} from './follow-up-suggestions';
import {
  windowMessages,
  skillIdsFromMessages,
  loadedSkillIds,
  pendingAwaitables,
} from './tracey-message-window';
import { buildAiTools } from './tracey-ai-tools';

/**
 * Infinite-loop safety backstop for the per-turn tool loop — **not** a user-facing turn limit.
 * `stopWhen` is structurally mandatory (without a stop condition the SDK ends the run after step 1,
 * so a tool-first turn produces no assistant text — see TRACEY.md), so this caps the loop at a
 * deliberately high value no legitimate flow approaches (the longest scripted flow, optimize-agent,
 * runs ~8 steps). It exists only to stop a pathological model from looping forever, never to
 * truncate a normal turn.
 */
export const MAX_TURN_STEPS = 64;

/**
 * A client-side {@link ChatTransport} that talks to Tracey's same-origin API endpoint
 * (`/api/tracey/{projectId}/openai/v1`). The API forwards each call to the project's provider and
 * captures it as an AgentCall attributed to the Tracey agent. Auth is the app's own JWT (injected
 * per request), so there is no CORS and no short-lived key. Tools execute in the browser via
 * {@link createTraceyTools}.
 */
export class TraceyTransport implements ChatTransport<UIMessage> {
  private readonly tools: ToolSet;
  private readonly model;
  /**
   * Correlation id for the in-flight turn. Sent on every upstream request of the turn so all of
   * its captured calls share one ConversationId, and attached to the assistant message's metadata
   * so the UI can deep-link the response to its ingested trace. Set per `sendMessages` call (which
   * the runtime awaits sequentially), so there's no cross-turn interleaving.
   */
  private currentTurnId: string | null = null;

  constructor(
    projectId: string,
    model: string,
    private readonly toolContext: TraceyToolContext,
    private readonly systemPrompt: string,
  ) {
    const openai = createOpenAI({
      // Relative URL → resolved against the app origin → same-origin (Vite/nginx proxies /api).
      baseURL: `/api/tracey/${projectId}/openai/v1`,
      // The real credential is the app JWT, injected by the custom fetch below; this is a placeholder.
      apiKey: 'app-jwt',
      fetch: async (input, init) => {
        const token = getAccessToken();
        const headers = new Headers(init?.headers);
        // The AI SDK pre-fills `Authorization: Bearer <apiKey>` from the placeholder above. Use the
        // real in-memory app JWT when we have one; otherwise DROP the header so the same-origin
        // session cookie authenticates. After a page reload the JWT is gone (it lives in memory
        // only — LocalAuthProvider restores the session from the cookie, not the token), and
        // leaving the bogus `app-jwt` bearer makes the backend reject it with 401 instead of
        // falling back to the cookie — i.e. Tracey gets no response on every post-reload turn.
        if (token) headers.set('Authorization', `Bearer ${token}`);
        else headers.delete('Authorization');
        if (this.currentTurnId) headers.set('x-proxytrace-conversation-id', this.currentTurnId);
        return fetch(input, { ...init, headers });
      },
    });
    // Chat Completions API (/chat/completions) — the default openai(id) targets the Responses API.
    this.model = openai.chat(model);
    this.tools = buildAiTools(toolContext);
  }

  async sendMessages(
    options: Parameters<ChatTransport<UIMessage>['sendMessages']>[0],
  ): Promise<ReadableStream<UIMessageChunk>> {
    // One id per turn: tags the turn's upstream calls (header, above) and the resulting assistant
    // message (metadata, below) with the same value so the UI can resolve the response → trace.
    const turnId = crypto.randomUUID();
    this.currentTurnId = turnId;
    const startedAt = performance.now();
    // The UI keeps the full thread, but the model only gets a window of recent messages — without
    // a cap, per-turn token cost grows linearly with conversation age forever.
    const windowed = windowMessages(options.messages);
    // A skill stays loaded for the whole conversation: re-derive the loaded set from the message
    // history (load_skill results persist there, surviving reloads and thread resets) so earlier
    // turns' bundles stay unlocked and `load_skill` can answer repeat loads compactly. Derived
    // from the *windowed* messages on purpose: if a playbook has been trimmed out of the model's
    // context, the skill must count as unloaded so a reload returns the full instructions again.
    const conversationSkills = this.toolContext.loadedSkillIds;
    conversationSkills.clear();
    for (const id of skillIdsFromMessages(windowed)) conversationSkills.add(id);
    const result = streamText({
      model: this.model,
      system: this.systemPrompt,
      // `ignoreIncompleteToolCalls` drops tool parts that never got a result — e.g. an
      // `await_actions` call orphaned by a page reload mid-wait. Without it the orphan converts
      // to a tool-call with no tool result, the OpenAI-shape endpoint rejects every subsequent
      // turn with a 400, and the conversation is permanently stuck.
      messages: await convertToModelMessages(windowed, { ignoreIncompleteToolCalls: true }),
      tools: this.tools,
      // Progressive tool disclosure: every tool is defined (so the wire capture stays complete),
      // but only CORE plus the bundles of skills loaded so far this conversation are offered to
      // the model on a given step. Loading a skill via `load_skill` unlocks its tools — a
      // dispatcher feel without a second model. (See `tool-access.ts`.)
      prepareStep: ({ steps }) => {
        const activeTools = activeToolNamesFor([...conversationSkills, ...loadedSkillIds(steps)]);
        // Proactive wait, enforced: once a step has produced an `awaitable` handle the model has
        // not yet passed to `await_actions`, the next step *must* call it — prompt instructions
        // alone proved unreliable, and a turn that ends right after `start_test_run` strands the
        // user re-prompting "is it done yet?". Forcing the tool (not 'required') means the model
        // can't detour; it still authors the args, so it batches every pending handle into the
        // one call. A wrong/missed id re-forces on the next step, capped by `stopWhen`.
        if (pendingAwaitables(steps).length > 0) {
          return {
            // The producing writes are only unlocked by bundles that include `await_actions`, but
            // a forced tool must be active, so guarantee it rather than rely on that invariant.
            activeTools: activeTools.includes('await_actions') ? activeTools : [...activeTools, 'await_actions'],
            toolChoice: { type: 'tool' as const, toolName: 'await_actions' },
          };
        }
        return { activeTools };
      },
      // `stopWhen` is structurally mandatory: without a stop condition the AI SDK ends the run
      // after the first step, so a turn that starts with a tool call produces no assistant text.
      // Keep looping (tool → result → model) until the model answers. MAX_TURN_STEPS is only a
      // high infinite-loop backstop (see its doc) — a normal turn finishes well below it.
      stopWhen: stepCountIs(MAX_TURN_STEPS),
      abortSignal: options.abortSignal,
    });
    // On finish, attach the turn's correlation id, token usage, and wall-clock duration to the
    // assistant message. assistant-ui surfaces these under `metadata.custom`; `MessageStatusBar`
    // shows them and uses the id to deep-link to the captured trace(s). `part.totalUsage` is the
    // SDK's usage aggregated across all tool-loop steps — i.e. the whole turn — which matches the
    // sum of the turn's ingested traces (including the cached-input portion). Emitted only on
    // finish, so the row stays hidden while the turn is still streaming.
    return result.toUIMessageStream({
      messageMetadata: ({ part }) =>
        part.type === 'finish'
          ? {
              custom: {
                traceConversationId: turnId,
                usage: {
                  inputTokens: part.totalUsage.inputTokens ?? 0,
                  cachedInputTokens: part.totalUsage.inputTokenDetails.cacheReadTokens ?? 0,
                  outputTokens: part.totalUsage.outputTokens ?? 0,
                  totalTokens: part.totalUsage.totalTokens ?? 0,
                },
                durationMs: Math.round(performance.now() - startedAt),
              },
            }
          : undefined,
    });
  }

  reconnectToStream(): Promise<ReadableStream<UIMessageChunk> | null> {
    return Promise.resolve(null);
  }

  /**
   * One small extra LLM call after a finished turn that proposes follow-up messages the user
   * might send next (see `follow-up-suggestions.ts`). It rides the same custom fetch as the turn
   * itself, so it authenticates the same way and — because `currentTurnId` is still set — is
   * ingested under the turn's ConversationId, grouping with the turn's traces. No tools, hard
   * output cap: this must stay cheap and can never loop.
   */
  async generateFollowUps(
    userText: string,
    assistantText: string,
    signal?: AbortSignal,
  ): Promise<string[]> {
    const result = await generateText({
      model: this.model,
      system: FOLLOW_UP_SYSTEM_PROMPT,
      prompt: buildFollowUpPrompt(userText, assistantText),
      // Generous for the ~40-token answer so a reasoning model's thinking budget doesn't starve
      // the visible output, still a hard cheapness cap.
      maxOutputTokens: 500,
      abortSignal: signal,
    });
    return parseFollowUps(result.text);
  }
}
