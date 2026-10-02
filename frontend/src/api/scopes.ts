import { api, qs } from './client';
import type { ScopeDetailDto, ScopeListItemDto, UpdateScopeRequest } from './models';

export const scopesApi = {
  list: (projectId: string) => api.get<ScopeListItemDto[]>(`/api/scopes${qs({ projectId })}`),
  get: (id: string) => api.get<ScopeDetailDto>(`/api/scopes/${id}`),
  update: (id: string, body: UpdateScopeRequest) => api.put<ScopeDetailDto>(`/api/scopes/${id}`, body),
  delete: (id: string) => api.del(`/api/scopes/${id}`),
};
