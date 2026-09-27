import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import type { AgentCallDto, TestSuiteListItemDto } from '../../api/models';
import useToast from '../../hooks/useToast';
import { Modal, ModalFooter } from '../overlays/Modal';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { FormField } from '../ui/FormField';
import { Input } from '../ui/Input';
import { ExpectedOutputEditor } from '../expected-output/ExpectedOutputEditor';
import { expectedFromResponse, toMessage, validateExpected } from '../expected-output/expectedOutput';
import { SuitePicker } from './SuitePicker';
import { useAddTraceTest } from './useAddTraceTest';

interface Props {
  trace: AgentCallDto;
  suites: TestSuiteListItemDto[];
  onClose: () => void;
}

export function AddTraceTestModal({ trace, suites, onClose }: Props) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const { show: toast } = useToast();
  const save = useAddTraceTest();
  const [suiteId, setSuiteId] = useState(suites[0]?.id ?? '');
  const [name, setName] = useState('');
  const [editing, setEditing] = useState(!trace.response);
  const [expected, setExpected] = useState(() => expectedFromResponse(trace.response));
  const creating = !suiteId;
  const valid = !!trace.agentId && (creating ? !!name.trim() : suites.some(s => s.id === suiteId))
    && (editing ? validateExpected(expected) : !!trace.response);
  const close = () => { if (!save.isPending) onClose(); };

  function submit() {
    if (!valid || !trace.agentId || save.isPending) return;
    save.mutate({
      traceId: trace.id, agentId: trace.agentId, suiteId, newSuiteName: name,
      expectedOutput: editing ? toMessage(expected) : undefined,
    }, {
      onSuccess: suite => {
        // eslint-disable-next-line lingui/no-unlocalized-strings -- toast tone token
        toast(t`Test added to ${suite.name}`, 'success', {
          action: { label: t`View suite`, onClick: () => navigate(`/suites?id=${suite.id}`) },
        });
        onClose();
      },
    });
  }

  return (
    <Modal title={t`Add to test suite`} onClose={close} size="md" footer={
      <ModalFooter onCancel={close} onSubmit={submit} loading={save.isPending} disabled={!valid}
        submitLabel={creating ? t`Create suite and add test` : t`Add to suite`} />
    }>
      <fieldset disabled={save.isPending} className="m-0 flex min-w-0 flex-col gap-4 border-0 p-0" data-testid="add-trace-test-modal">
        <div className="max-h-48 overflow-y-auto">
          <SuitePicker suites={suites} value={suiteId} onChange={setSuiteId} />
        </div>
        <Button variant="secondary" size="sm" data-testid="trace-create-suite" onClick={() => setSuiteId('')}>
          <Trans>Create suite</Trans>
        </Button>
        {creating && (
          <FormField label={t`Suite name`}>
            <Input value={name} onChange={e => setName(e.target.value)} data-testid="trace-new-suite-name"
              placeholder={t`My regression suite`} autoFocus />
          </FormField>
        )}
        <p className="m-0 text-body text-secondary">
          <Trans>Save the recorded response as the expected output, or correct it to capture the behavior you want.</Trans>
        </p>
        <Checkbox checked={editing} disabled={!trace.response} onChange={e => setEditing(e.target.checked)}
          label={t`Edit expected output`} data-testid="trace-edit-expected" />
        {editing && <ExpectedOutputEditor value={expected} tools={trace.tools} onChange={setExpected} />}
        {save.isError && <p role="alert" className="m-0 text-body text-danger">{save.error.message}</p>}
      </fieldset>
    </Modal>
  );
}
