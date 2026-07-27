import type { ApiKeyScope } from '../../api/models';

// ApiKeyScope members and the `/mcp` path are domain identifiers, not translatable UI copy. Keeping
// them in this plain-.ts module (where the lingui lint rule does not apply) keeps the feature's .tsx
// literal-free.
export const SCOPE_ORDER: ApiKeyScope[] = ['Ingestion', 'Passthrough', 'McpRead', 'McpWrite', 'ApiRead', 'ApiWrite'];

/** Single-letter mnemonic shown on the capability chip. Lowercase r/w mark the REST API pair. */
export const SCOPE_LETTER: Record<ApiKeyScope, string> = {
  Ingestion: 'P',
  Passthrough: 'F',
  McpRead: 'R',
  McpWrite: 'W',
  ApiRead: 'r',
  ApiWrite: 'w',
};

export const hasIngestion = (scopes: ApiKeyScope[]) => scopes.includes('Ingestion');

export const hasMcp = (scopes: ApiKeyScope[]) => scopes.includes('McpRead') || scopes.includes('McpWrite');

export const hasApi = (scopes: ApiKeyScope[]) => scopes.includes('ApiRead') || scopes.includes('ApiWrite');

export const mcpEndpointUrl = (origin: string) => `${origin}/mcp`;

export const apiBaseUrl = (origin: string) => `${origin}/api`;
