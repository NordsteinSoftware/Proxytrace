import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import useCurrentProject from '../../../hooks/useCurrentProject';
import { Button, IconButton } from '../../../components/ui/Button';
import { EmptyState } from '../../../components/ui/EmptyState';
import { Input } from '../../../components/ui/Input';
import { Select } from '../../../components/ui/Select';
import { FormField } from '../../../components/ui/FormField';
import { SkeletonList } from '../../../components/ui/Skeleton';
import { ConfirmDialog } from '../../../components/overlays/ConfirmDialog';
import { TrashIcon, EditIcon, CheckIcon, XIcon } from '../../../components/icons';
import { fmtDate } from '../../../lib/format';
import { endpointLabel } from '../projectsMeta';
import { useProject, useUpdateProject, useDeleteProject } from '../hooks/useProjects';
import useModelEndpoints from '../../../hooks/useModelEndpoints';
import { SectionHeader } from '../components/SectionHeader';

/** General settings for the active project: rename, system endpoint, delete. */
export function GeneralSection() {
  const { t } = useLingui();
  const { currentProjectId } = useCurrentProject();
  const navigate = useNavigate();

  const { data: endpoints = [] } = useModelEndpoints();
  const { data: project, isLoading } = useProject(currentProjectId);

  const [editName, setEditName] = useState(false);
  const [editEndpoint, setEditEndpoint] = useState(false);
  const [nameDraft, setNameDraft] = useState('');
  const [endpointDraft, setEndpointDraft] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(false);

  const updateProject = useUpdateProject();
  const deleteProject = useDeleteProject();

  const finishEdit = () => { setEditName(false); setEditEndpoint(false); };

  if (!currentProjectId) {
    return (
      <EmptyState
        title={t`No project selected`}
        description={t`Create a project in the Projects section to configure it here.`}
      />
    );
  }

  return (
    <div className="w-full min-w-0 flex flex-col" data-testid="settings-general">
      <SectionHeader title={t`General`} subtitle={t`Name, system endpoint, and lifecycle for the active project.`} />

      {isLoading || !project ? (
        <SkeletonList rows={3} height={56} gap={10} />
      ) : (
        <div className="bg-card-2 border border-hairline rounded-lg p-4 flex flex-col gap-3 max-w-[760px]">
          {/* Name */}
          <FormField label={t`Project name`}>
            {editName ? (
              <div className="flex items-center gap-2">
                <Input autoFocus value={nameDraft} onChange={e => setNameDraft(e.target.value)} data-testid="project-name-input" />
                <IconButton
                  data-write
                  aria-label={t`Save name`}
                  onClick={() => updateProject.mutate(
                    { id: project.id, req: { name: nameDraft.trim(), systemEndpointId: project.systemEndpointId } },
                    { onSuccess: finishEdit },
                  )}
                  disabled={!nameDraft.trim() || nameDraft.trim() === project.name}
                >
                  <CheckIcon size={14} />
                </IconButton>
                <IconButton aria-label={t`Cancel`} onClick={() => { setEditName(false); setNameDraft(project.name); }}>
                  <XIcon size={14} />
                </IconButton>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <span className="text-title text-primary font-semibold" data-testid="project-name">{project.name}</span>
                <IconButton data-write aria-label={t`Edit name`} onClick={() => { setNameDraft(project.name); setEditName(true); }}>
                  <EditIcon size={14} />
                </IconButton>
              </div>
            )}
          </FormField>

          {/* System endpoint */}
          <FormField label={t`System endpoint`}>
            {editEndpoint ? (
              <div className="flex items-center gap-2">
                <div className="flex-1 min-w-0">
                  <Select autoFocus value={endpointDraft} onValueChange={setEndpointDraft}>
                    {endpoints.map(e => (
                      <option key={e.id} value={e.id}>{e.providerName} · {e.modelName}</option>
                    ))}
                  </Select>
                </div>
                <IconButton
                  data-write
                  aria-label={t`Save endpoint`}
                  onClick={() => updateProject.mutate(
                    { id: project.id, req: { name: project.name, systemEndpointId: endpointDraft } },
                    { onSuccess: finishEdit },
                  )}
                  disabled={endpointDraft === project.systemEndpointId}
                >
                  <CheckIcon size={14} />
                </IconButton>
                <IconButton aria-label={t`Cancel`} onClick={() => { setEditEndpoint(false); setEndpointDraft(project.systemEndpointId); }}>
                  <XIcon size={14} />
                </IconButton>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <span className="text-title text-primary">{endpointLabel(endpoints, project.systemEndpointId)}</span>
                <IconButton data-write aria-label={t`Edit endpoint`} onClick={() => { setEndpointDraft(project.systemEndpointId); setEditEndpoint(true); }}>
                  <EditIcon size={14} />
                </IconButton>
              </div>
            )}
          </FormField>

          <div className="text-body-sm text-muted border-t border-hairline pt-4">
            <Trans>Created {fmtDate(project.createdAt)} · Updated {fmtDate(project.updatedAt)}</Trans>
          </div>

          <div>
            <Button
              variant="dangerOutline"
              size="sm"
              data-testid="project-delete-btn"
              leftIcon={<TrashIcon size={14} />}
              onClick={() => setConfirmDelete(true)}
            >
              <Trans>Delete project</Trans>
            </Button>
          </div>
        </div>
      )}

      {confirmDelete && project && (
        <ConfirmDialog
          entityName={project.name}
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() => deleteProject.mutate(project.id, {
            // ProjectProvider falls back to the oldest remaining project automatically once the
            // deleted id is gone from the list; send the user to Projects to pick/confirm.
            onSuccess: () => { setConfirmDelete(false); navigate('/settings/projects'); },
          })}
          loading={deleteProject.isPending}
        />
      )}
    </div>
  );
}
