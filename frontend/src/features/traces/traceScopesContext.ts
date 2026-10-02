import { createContext, useContext } from 'react';
import type { ScopeListItemDto } from '../../api/models';

const NO_SCOPES: ReadonlyMap<string, ScopeListItemDto> = new Map();

/**
 * The current project's scopes by id, provided once by the trace list so every row can label its
 * scope tag without each row subscribing to the query itself. Defaults to empty, so a row rendered
 * outside a list (a spec, a standalone preview) simply shows no tag.
 */
export const TraceScopesContext = createContext<ReadonlyMap<string, ScopeListItemDto>>(NO_SCOPES);

/** The scope a trace row was sent under, or `undefined` when unscoped / not (yet) loaded. */
export function useTraceScope(scopeId: string | null): ScopeListItemDto | undefined {
  const scopes = useContext(TraceScopesContext);
  return scopeId ? scopes.get(scopeId) : undefined;
}
