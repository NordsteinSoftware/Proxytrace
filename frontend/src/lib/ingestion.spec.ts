import { afterEach, describe, expect, it, vi } from 'vitest';
import { ingestionUrl, projectSlug, resolveProxyBase, scopeKey } from './ingestion';

describe('projectSlug', () => {
  it.each([
    ['Showcase Project', 'showcase-project'],
    ['OpenAI (demo)', 'openai-demo'],
    ['  leading and trailing  ', 'leading-and-trailing'],
    ['Multiple   spaces', 'multiple-spaces'],
    ['already-slugged', 'already-slugged'],
    ['Mixed_Case_Underscores', 'mixed-case-underscores'],
  ])('slugifies %j -> %j', (input, expected) => {
    expect(projectSlug(input)).toBe(expected);
  });
});

describe('ingestionUrl', () => {
  it('builds {base}/{slug}/openai/v1 against an explicit base', () => {
    expect(ingestionUrl('Showcase Project', 'https://host')).toBe(
      'https://host/showcase-project/openai/v1',
    );
  });

  it('trims a trailing slash on the base', () => {
    expect(ingestionUrl('Showcase Project', 'https://host/')).toBe(
      'https://host/showcase-project/openai/v1',
    );
  });
});

describe('scopeKey', () => {
  // Parity cases with the backend ScopeKeyTests (Proxytrace.Domain.Tests/ScopeTests.cs).
  it.each([
    ['support-agents', 'support-agents'],
    ['Support Agents', 'support-agents'],
    ['support_agents', 'support-agents'],
    ['  --Support__Agents--  ', 'support-agents'],
    ['Kundenservice Überweisung', 'kundenservice-überweisung'],
    ['v2!', 'v2'],
  ])('normalises %j -> %j', (input, expected) => {
    expect(scopeKey(input)).toBe(expected);
  });

  it.each(['', '   ', '!!!', 'openai', 'OpenAI', 'v1'])('rejects unusable or reserved %j', input => {
    expect(scopeKey(input)).toBe('');
  });

  it('truncates an overlong key without leaving a trailing hyphen', () => {
    expect(scopeKey(`${'a'.repeat(63)} tail`)).toBe('a'.repeat(63));
  });
});

describe('ingestionUrl with a scope', () => {
  it('adds the canonical scope segment', () => {
    expect(ingestionUrl('Translogica', 'https://host', 'Support Agents')).toBe(
      'https://host/translogica/support-agents/openai/v1',
    );
  });

  it('omits an unusable scope', () => {
    expect(ingestionUrl('Translogica', 'https://host', 'openai')).toBe('https://host/translogica/openai/v1');
  });
});

describe('resolveProxyBase', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('prefers the backend-advertised URL', () => {
    expect(resolveProxyBase('http://localhost:5102')).toBe('http://localhost:5102');
  });

  it('trims a trailing slash on the advertised URL', () => {
    expect(resolveProxyBase('http://localhost:5102/')).toBe('http://localhost:5102');
  });

  it.each([null, undefined, '', '   '])(
    'falls back to the page origin when the advertised URL is %j',
    advertised => {
      // The vitest environment is node — no real window, so stub the origin.
      vi.stubGlobal('window', { location: { origin: 'http://page-origin:5101' } });
      expect(resolveProxyBase(advertised)).toBe('http://page-origin:5101');
    },
  );
});
