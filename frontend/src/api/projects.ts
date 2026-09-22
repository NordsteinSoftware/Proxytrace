import { api, qs } from './client';
import type { PagedResult, ProjectDto, ProjectListItemDto, ProjectMemberDto } from './models';

export interface CreateProjectRequest {
  name: string;
  systemEndpointId: string;
  memberIds?: string[];
}

export interface UpdateProjectRequest {
  name: string;
  systemEndpointId: string;
}

export const projectsApi = {
  list: (params?: { page?: number; pageSize?: number }) =>
    api.get<PagedResult<ProjectListItemDto>>(`/api/projects${qs(params ?? {})}`),
  get: (id: string) => api.get<ProjectDto>(`/api/projects/${id}`),
  create: (req: CreateProjectRequest) => api.post<ProjectDto>('/api/projects', req),
  update: (id: string, req: UpdateProjectRequest) => api.put<ProjectDto>(`/api/projects/${id}`, req),
  updateDefaultUpstreamProvider: (id: string, providerId: string | null) =>
    api.put<ProjectDto>(`/api/projects/${id}/default-upstream-provider`, { providerId }),
  delete: (id: string) => api.del(`/api/projects/${id}`),

  getMembers: (id: string) => api.get<ProjectMemberDto[]>(`/api/projects/${id}/members`),
  addMember: (id: string, userId: string) => api.post<ProjectDto>(`/api/projects/${id}/members/${userId}`),
  removeMember: (id: string, userId: string) => api.del(`/api/projects/${id}/members/${userId}`),
};
