import { api, qs } from './client';
import type { PagedResult, SessionDto } from './models';

export const sessionsApi = {
  list: (params: { projectId: string; page?: number; pageSize?: number }) =>
    api.get<PagedResult<SessionDto>>(`/api/sessions${qs(params)}`),
  get: (id: string) => api.get<SessionDto>(`/api/sessions/${id}`),
};
