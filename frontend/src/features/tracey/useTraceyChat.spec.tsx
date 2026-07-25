// @vitest-environment jsdom
/**
 * Regression tests for conversation-history restore in {@link useTraceyChat}. Snapshots persist to
 * localStorage as JSON in the AI SDK `UIMessage` format (`ConversationSnapshot`) and round-trip via
 * the runtime's `exportExternalState()`/`importExternalState()`. The runtime's plain
 * `export()`/`import()` pair MUST NOT be used for storage: its ThreadMessages reference the AI SDK
 * messages through a Symbol-keyed property that JSON.stringify drops, so an imported snapshot
 * silently rebuilt an empty chat — clicking a past conversation (or reloading) showed nothing.
 */
import { describe, it, vi, beforeEach, expect } from 'vitest';

// Node 22+ ships a stub `localStorage` without the Storage API; replace with an in-memory one.
const lsData = new Map<string, string>();
const memLocalStorage = {
  getItem: (k: string) => (lsData.has(k) ? (lsData.get(k) as string) : null),
  setItem: (k: string, v: string) => { lsData.set(k, String(v)); },
  removeItem: (k: string) => { lsData.delete(k); },
  clear: () => { lsData.clear(); },
  key: (i: number) => [...lsData.keys()][i] ?? null,
  get length() { return lsData.size; },
};
Object.defineProperty(globalThis, 'localStorage', { value: memLocalStorage, writable: true });
Object.defineProperty(window, 'localStorage', { value: memLocalStorage, writable: true });

import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { AssistantRuntimeProvider } from '@assistant-ui/react';

vi.mock('react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@tanstack/react-query', () => ({
  useQuery: () => ({ data: undefined, status: 'pending' }),
}));
vi.mock('../../api/tracey', () => ({ traceyApi: { getSession: vi.fn() } }));
vi.mock('../../hooks/useLicense', () => ({ useFeature: () => false }));
vi.mock('../../hooks/useCurrentProject', () => ({
  default: () => ({ currentProject: { id: 'proj1' } }),
}));
vi.mock('../../auth/useCurrentUser', () => ({ useCurrentUser: () => ({ email: 'u@x' }) }));
vi.mock('../../contexts/KioskContext', () => ({ useKiosk: () => ({ interactive: false }) }));
vi.mock('./tracey-artifact-store', () => ({
  collectArtifactRefs: () => [],
  pruneArtifacts: () => Promise.resolve(),
}));

import { useTraceyChat, type TraceyChat } from './useTraceyChat';
import type { ConversationSnapshot } from './tracey-storage';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

const USER = 'u@x';
const PROJ = 'proj1';

function seedConversation(id: string, texts: [user: string, assistant: string], activeId: string | null) {
  const snapshot: ConversationSnapshot = {
    headId: `${id}-m2`,
    messages: [
      { parentId: null, message: { id: `${id}-m1`, role: 'user', parts: [{ type: 'text', text: texts[0] }] } },
      { parentId: `${id}-m1`, message: { id: `${id}-m2`, role: 'assistant', parts: [{ type: 'text', text: texts[1] }] } },
    ],
  };
  localStorage.setItem(
    `proxytrace.tracey.conversation:${USER}:${PROJ}:${id}`,
    JSON.stringify(snapshot),
  );
  const indexKey = `proxytrace.tracey.conversations:${USER}:${PROJ}`;
  const existing = localStorage.getItem(indexKey);
  const index = existing
    ? (JSON.parse(existing) as { version: 1; activeId: string | null; items: unknown[] })
    : { version: 1 as const, activeId: null, items: [] };
  index.items.push({ id, title: texts[0], createdAt: 1, updatedAt: 2, messageCount: 2 });
  index.activeId = activeId;
  localStorage.setItem(indexKey, JSON.stringify(index));
}

// Test-harness escape hatch: the spec drives the hook imperatively, so the latest hook result is
// captured into a module-level ref after each render (a spec-only pattern).
const chatRef: { current: TraceyChat | null } = { current: null };
function Host() {
  const chat = useTraceyChat();
  useEffect(() => { chatRef.current = chat; });
  return <AssistantRuntimeProvider runtime={chat.runtime}>{null}</AssistantRuntimeProvider>;
}

const flush = () => act(async () => { await new Promise(r => setTimeout(r, 50)); });

async function mountHost(): Promise<Root> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => { root.render(<Host />); });
  await flush();
  return root;
}

function threadTexts(chat: TraceyChat): string[] {
  return chat.runtime.thread.getState().messages
    .map(m => m.content.map(p => (p.type === 'text' ? p.text : '')).join(''));
}

describe('useTraceyChat conversation restore', () => {
  beforeEach(() => {
    localStorage.clear();
    chatRef.current = null;
  });

  it('selecting a stored conversation from an empty current thread loads its messages', async () => {
    seedConversation('conv-a', ['hello there', 'hi back'], null);
    await mountHost();
    const chat = chatRef.current as TraceyChat;
    expect(chat.runtime.thread.getState().messages).toHaveLength(0);

    await act(async () => { chat.selectConversation('conv-a'); });
    await flush();

    expect((chatRef.current as TraceyChat).activeConversationId).toBe('conv-a');
    expect(threadTexts(chatRef.current as TraceyChat)).toEqual(['hello there', 'hi back']);
  });

  it('restores the active conversation on mount (page reload)', async () => {
    seedConversation('conv-a', ['hello there', 'hi back'], 'conv-a');
    await mountHost();

    const chat = chatRef.current as TraceyChat;
    expect(chat.activeConversationId).toBe('conv-a');
    expect(threadTexts(chat)).toEqual(['hello there', 'hi back']);
  });

  it('switches between two stored conversations', async () => {
    seedConversation('conv-a', ['first question', 'first answer'], null);
    seedConversation('conv-b', ['second question', 'second answer'], 'conv-a');
    await mountHost();
    await flush();
    expect(threadTexts(chatRef.current as TraceyChat)).toEqual(['first question', 'first answer']);

    await act(async () => { (chatRef.current as TraceyChat).selectConversation('conv-b'); });
    await flush();

    expect((chatRef.current as TraceyChat).activeConversationId).toBe('conv-b');
    expect(threadTexts(chatRef.current as TraceyChat)).toEqual(['second question', 'second answer']);
  });

  it('falls back to an empty thread for a legacy-format snapshot without crashing', async () => {
    // Pre-fix snapshots stored assistant-ui ThreadMessages (`content`, not `parts`).
    localStorage.setItem(
      `proxytrace.tracey.conversation:${USER}:${PROJ}:conv-old`,
      JSON.stringify({
        headId: 'm1',
        messages: [{ parentId: null, message: { id: 'm1', role: 'user', content: [{ type: 'text', text: 'old' }] } }],
      }),
    );
    localStorage.setItem(
      `proxytrace.tracey.conversations:${USER}:${PROJ}`,
      JSON.stringify({
        version: 1,
        activeId: 'conv-old',
        items: [{ id: 'conv-old', title: 'old', createdAt: 1, updatedAt: 2, messageCount: 1 }],
      }),
    );
    await mountHost();

    const chat = chatRef.current as TraceyChat;
    expect(chat.conversations).toHaveLength(1);
    expect(chat.runtime.thread.getState().messages).toHaveLength(0);
  });
});
