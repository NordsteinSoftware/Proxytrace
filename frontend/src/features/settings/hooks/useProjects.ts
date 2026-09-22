import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { projectsApi } from '../../../api/projects';
import { QUERY_KEYS } from '../../../api/query-keys';
import { LIST_PAGE_SIZE } from '../../../lib/constants';

type ProjectFields = { name: string; systemEndpointId: string };

/** All projects for the master list. */
export function useProjects() {
  return useQuery({
    queryKey: QUERY_KEYS.projects,
    queryFn: () => projectsApi.list({ pageSize: LIST_PAGE_SIZE }),
  });
}

/** Full detail (incl. members) for one project. */
export function useProject(projectId: string | null) {
  return useQuery({
    queryKey: QUERY_KEYS.project(projectId ?? 'none'),
    queryFn: () => projectsApi.get(projectId ?? ''),
    enabled: !!projectId,
  });
}

function useInvalidateProject() {
  const qc = useQueryClient();
  return (projectId?: string) => {
    qc.invalidateQueries({ queryKey: QUERY_KEYS.projects });
    if (projectId) qc.invalidateQueries({ queryKey: QUERY_KEYS.project(projectId) });
  };
}

/** Creates a project; invalidates the list. Caller selects it via `mutate`'s onSuccess. */
export function useCreateProject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: ProjectFields) => projectsApi.create(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.projects }),
  });
}

/** Updates a project's name and/or system endpoint. */
export function useUpdateProject() {
  const invalidate = useInvalidateProject();
  return useMutation({
    mutationFn: (args: { id: string; req: ProjectFields }) => projectsApi.update(args.id, args.req),
    onSuccess: (_result, args) => invalidate(args.id),
  });
}

/** Sets or clears anonymous forwarding for a project. */
export function useUpdateDefaultUpstreamProvider() {
  const invalidate = useInvalidateProject();
  return useMutation({
    mutationFn: (args: { id: string; providerId: string | null }) =>
      projectsApi.updateDefaultUpstreamProvider(args.id, args.providerId),
    onSuccess: (_result, args) => invalidate(args.id),
  });
}

/** Deletes a project; invalidates the list. */
export function useDeleteProject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => projectsApi.delete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: QUERY_KEYS.projects }),
  });
}

/** Adds a member to a project. */
export function useAddMember() {
  const invalidate = useInvalidateProject();
  return useMutation({
    mutationFn: (args: { projectId: string; userId: string }) => projectsApi.addMember(args.projectId, args.userId),
    onSuccess: (_result, args) => invalidate(args.projectId),
  });
}

/** Removes a member from a project. */
export function useRemoveMember() {
  const invalidate = useInvalidateProject();
  return useMutation({
    mutationFn: (args: { projectId: string; userId: string }) => projectsApi.removeMember(args.projectId, args.userId),
    onSuccess: (_result, args) => invalidate(args.projectId),
  });
}
