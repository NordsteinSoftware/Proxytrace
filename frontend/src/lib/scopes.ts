import type { ScopeListItemDto } from '../api/models';


/** Minimal shape a scope label needs — satisfied by both the list and the detail DTO. */
interface ScopeNaming {
  key: string;
  displayName: string | null;
}

/** What the UI calls a scope: its curated display name, else its canonical key. */
export function scopeLabel(scope: ScopeNaming): string {
  return scope.displayName?.trim() || scope.key;
}

/** Scopes indexed by id, for resolving a trace's `scopeId` to a label without another request. */
export function scopesById(scopes: readonly ScopeListItemDto[]): ReadonlyMap<string, ScopeListItemDto> {
  return new Map(scopes.map(s => [s.id, s]));
}
