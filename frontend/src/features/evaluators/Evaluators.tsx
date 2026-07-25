import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useSelectedId } from '../../hooks/useSelectedId';
import { useLocalStorageState } from '../../hooks/useLocalStorageState';
import { EvaluatorKind, type EvaluatorDetailDto } from '../../api/models';
import { Modal, ModalFooter } from '../../components/overlays/Modal';
import { type RangeKey } from '../../lib/time-range';
import { META, initForm, type EvaluatorFormState } from './evaluatorMeta';
import { EvaluatorForm } from './EvaluatorForm';
import { LIST_RAIL_COLS } from '../../components/ui/ListRail';
import { EvalRail } from './components/EvalRail';
import { EvaluatorDetail } from './components/EvaluatorDetail';
import { EmptyDetail } from './components/EmptyDetail';
import { NewEvaluatorModal } from './components/NewEvaluatorModal';
import {
  useEvaluatorsOverview,
  useAgenticPresets,
} from './hooks/useEvaluatorQueries';
import {
  useCreateEvaluator,
  useUpdateEvaluator,
  useDeleteEvaluator,
  formFromEvaluator,
} from './hooks/useEvaluatorMutations';

export default function Evaluators() {
  const navigate = useNavigate();
  const { t, i18n } = useLingui();
  // Selection lives in ?id= (survives refresh, shareable). The default (first
  // evaluator) is derived below, not written to the URL.
  const [selectedId, setSelectedId] = useSelectedId();
  const { currentProjectId } = useCurrentProject();

  // Persisted so the chosen window survives switching evaluators and reloads (mirrors useAgentStats).
  // eslint-disable-next-line lingui/no-unlocalized-strings -- localStorage key + RangeKey enum token, not UI copy
  const [range, setRange] = useLocalStorageState<RangeKey>('evaluator-stats-range', '7d');
  const [showNew, setShowNew] = useState(false);
  const [pickedKind, setPickedKind] = useState<EvaluatorKind | null>(null);
  const [createForm, setCreateForm] = useState<EvaluatorFormState>(initForm());
  const [editTargetId, setEditTargetId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<EvaluatorFormState>(initForm());
  const [deleteTargetId, setDeleteTargetId] = useState<string | null>(null);

  const { evaluators, suites, sparklines, isLoading } = useEvaluatorsOverview(currentProjectId);
  const { data: presets = [] } = useAgenticPresets();
  const { sparklineById, avgScoreById } = sparklines;

  const effectiveId = (selectedId && evaluators.some(e => e.id === selectedId))
    ? selectedId
    : evaluators[0]?.id ?? null;
  const selected = evaluators.find(e => e.id === effectiveId) ?? null;
  const editTarget = evaluators.find(e => e.id === editTargetId) ?? null;
  const deleteTarget = evaluators.find(e => e.id === deleteTargetId) ?? null;

  const attachedSuites = selected
    ? suites
        .filter(s => s.evaluatorIds.includes(selected.id))
        .map(s => ({ id: s.id, name: s.name, agentId: s.agentId, agentName: s.agentName }))
    : [];

  const createEval = useCreateEvaluator();
  const updateEval = useUpdateEvaluator();
  const deleteEval = useDeleteEvaluator();

  function submitCreate() {
    if (!pickedKind || !currentProjectId) return;
    createEval.mutate(
      { kind: pickedKind, projectId: currentProjectId, form: createForm },
      {
        onSuccess: e => {
          setSelectedId(e.id);
          setShowNew(false);
          setPickedKind(null);
          setCreateForm(initForm());
        },
      },
    );
  }

  function submitUpdate() {
    if (!editTarget) return;
    updateEval.mutate(
      { id: editTarget.id, kind: editTarget.kind, form: editForm },
      { onSuccess: () => setEditTargetId(null) },
    );
  }

  function submitDelete() {
    if (!deleteTargetId) return;
    const targetId = deleteTargetId;
    deleteEval.mutate(targetId, {
      onSuccess: () => {
        if (selectedId === targetId) setSelectedId(null);
        setDeleteTargetId(null);
      },
    });
  }

  function openEdit(e: EvaluatorDetailDto) {
    setEditTargetId(e.id);
    setEditForm(formFromEvaluator(e));
  }

  function openNew() {
    setShowNew(true);
    setPickedKind(null);
    setCreateForm(initForm());
  }

  return (
    <div className="h-full flex flex-col min-h-0">
      <div className={`flex-1 grid ${LIST_RAIL_COLS} gap-3.5 min-h-0`}>
        <EvalRail
          evaluators={evaluators}
          isLoading={isLoading}
          selectedId={effectiveId}
          onSelect={id => setSelectedId(id)}
          onNew={openNew}
          sparklineById={sparklineById}
          avgScoreById={avgScoreById}
        />

        <main className="min-w-0 overflow-y-auto flex flex-col pr-2.5">
          {selected ? (
            <EvaluatorDetail
              evaluator={selected}
              attachedSuites={attachedSuites}
              range={range}
              onRangeChange={setRange}
              onEdit={() => openEdit(selected)}
              onDelete={() => setDeleteTargetId(selected.id)}
              onTestBench={id => navigate(`/evaluator-playground?id=${id}`)}
            />
          ) : (
            <EmptyDetail hasAny={evaluators.length > 0} onCreate={openNew} />
          )}
        </main>
      </div>

      {showNew && (
        <NewEvaluatorModal
          pickedKind={pickedKind}
          setPickedKind={setPickedKind}
          form={createForm}
          setForm={setCreateForm}
          presets={presets}
          onClose={() => setShowNew(false)}
          onSubmit={submitCreate}
          loading={createEval.isPending}
        />
      )}

      {editTarget && (
        <Modal
          title={t`Edit ${META[editTarget.kind] ? i18n._(META[editTarget.kind].label) : 'Evaluator'}`}
          onClose={() => setEditTargetId(null)}
          maxWidth={520}
          footer={
            <ModalFooter
              onCancel={() => setEditTargetId(null)}
              onSubmit={submitUpdate}
              submitLabel={updateEval.isPending ? t`Saving…` : t`Save`}
              loading={updateEval.isPending}
            />
          }
        >
          <EvaluatorForm form={editForm} setForm={setEditForm} kind={editTarget.kind} presets={presets} showPresetPicker={false} />
        </Modal>
      )}

      {deleteTarget && (
        <Modal
          title={t`Delete "${deleteTarget.name}"`}
          onClose={() => setDeleteTargetId(null)}
          footer={
            <ModalFooter
              onCancel={() => setDeleteTargetId(null)}
              onSubmit={submitDelete}
              submitLabel={deleteEval.isPending ? t`Deleting…` : t`Delete`}
              loading={deleteEval.isPending}
              danger
            />
          }
        >
          <p className="text-title text-secondary m-0">
            <Trans>This will permanently remove <strong>{deleteTarget.name}</strong> and detach it from all test suites.</Trans>
          </p>
        </Modal>
      )}
    </div>
  );
}
