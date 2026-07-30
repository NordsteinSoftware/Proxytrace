import { beforeAll, describe, it, expect } from 'vitest';
import { ModelProviderKind } from '../../api/models';
import { i18n } from '../../i18n';
import { kindLabel, kindColor, isDefaultEndpoint, isAzureEndpoint } from './providerMeta';

// Activate an empty catalog so i18n._() resolves MessageDescriptors to their source strings.
beforeAll(() => i18n.loadAndActivate({ locale: 'en', messages: {} }));

describe('kindLabel', () => {
  it('maps known kinds', () => {
    expect(i18n._(kindLabel(ModelProviderKind.OpenAi))).toBe('OpenAI');
    expect(i18n._(kindLabel(ModelProviderKind.OpenAiCompatible))).toBe('OpenAI-compatible');
  });
  it('falls back to Unknown', () => {
    expect(i18n._(kindLabel(ModelProviderKind.Unknown))).toBe('Unknown');
  });
});

describe('kindColor', () => {
  it('maps each kind to a token', () => {
    expect(kindColor(ModelProviderKind.OpenAi)).toBe('var(--success)');
    expect(kindColor(ModelProviderKind.OpenAiCompatible)).toBe('var(--teal)');
    expect(kindColor(ModelProviderKind.Unknown)).toBe('var(--text-muted)');
  });
});

describe('isDefaultEndpoint', () => {
  it('treats the canonical OpenAI endpoint as default', () => {
    expect(isDefaultEndpoint(ModelProviderKind.OpenAi, 'https://api.openai.com/v1')).toBe(true);
  });
  it('treats custom endpoints as non-default', () => {
    expect(isDefaultEndpoint(ModelProviderKind.OpenAi, 'https://proxy.internal/v1')).toBe(false);
    expect(isDefaultEndpoint(ModelProviderKind.OpenAiCompatible, 'https://x.openai.azure.com/')).toBe(false);
  });
});

describe('isAzureEndpoint', () => {
  it('detects azure hosts', () => {
    expect(isAzureEndpoint('https://r.openai.azure.com/')).toBe(true);
    expect(isAzureEndpoint('https://api.openai.com/v1')).toBe(false);
  });
});
