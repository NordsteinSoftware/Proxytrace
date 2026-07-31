import { useState } from 'react';
import type { AgentCallDto, TestSuiteListItemDto } from '../../api/models';
import { usePromoteTrace } from './usePromoteTrace';
import { agentColor, EVALUATOR_KIND_COLOR } from '../../lib/colors';
import { fmtRelative, fmtPct100 } from '../../lib/format';
import { PlusIcon } from '../icons';
import { ColoredBadge } from '../ui/ColoredBadge';
import { MessageBubble } from '../ui/MessageBubble';
import { ExpectedOutputEditor } from '../expected-output/ExpectedOutputEditor';
import {
  expectedFromResponse,
  toMessage,
  validateExpected,
} from '../expected-output/expectedOutput';
import useToast from '../../hooks/useToast';
import { Button } from '../ui/Button';
import { Modal } from '../overlays/Modal';
import { SuitePicker } from './SuitePicker';
import { Trans, Plural, useLingui } from '@lingui/react/macro';

interface Props {
  trace: AgentCallDto;
  suites: TestSuiteListItemDto[];
  onClose: () => void;
}

export function PromoteModal({ trace, suites, onClose }: Props) {
  const aColor = agentColor(trace.agentId ?? trace.id);

  const [suiteId, setSuiteId] = useState<string>(suites[0]?.id ?? '');
  const [expected, setExpected] = useState(() => expectedFromResponse(trace.response ?? null));
  const { show: toast } = useToast();
  const { t } = useLingui();
  const agentLabel = trace.agentName ?? t`Agent`;

  const inputMessages = trace.request.filter(m => m.role !== 'system');
  const hasSystem = trace.request.some(m => m.role === 'system');
  const expectedValid = validateExpected(expected);

  const selectedSuite = suites.find(s => s.id === suiteId) ?? null;

  const addCase = usePromoteTrace((suiteName) => {
    // eslint-disable-next-line lingui/no-unlocalized-strings -- toast tone token, not UI copy
    toast(t`Added to ${suiteName}`, 'success');
    onClose();
  });

  const errorMsg = addCase.isError
    ? ((addCase.error as Error).message || t`Failed to add test case`)
    : null;
  const submitDisabled = !suiteId || !expectedValid || addCase.isPending;

  return (
    <Modal
      title={t`Add test`}
      onClose={onClose}
      maxWidth={980}
      headerActions={
        <>
          <ColoredBadge color={aColor} label={agentLabel} dot size="md" />
          <span className="mono text-body-sm text-muted">{trace.id.slice(0, 10)}…</span>
        </>
      }
      footer={
        <>
          {errorMsg && (
            <span className="mr-auto self-center min-w-0 truncate text-body-sm text-danger">{errorMsg}</span>
          )}
          <Button variant="secondary" onClick={onClose}><Trans>Cancel</Trans></Button>
          <Button
            variant="primary"
            data-testid="promote-submit-btn"
            onClick={() => addCase.mutate({ suiteId, traceId: trace.id, expected: toMessage(expected) })}
            disabled={submitDisabled}
            loading={addCase.isPending}
            leftIcon={!addCase.isPending && <PlusIcon strokeWidth={2.5} size={13} />}
          >
            {addCase.isPending ? <Trans>Adding…</Trans> : <Trans>Add to suite</Trans>}
          </Button>
        </>
      }
    >
      <div data-testid="promote-modal" className="flex flex-col min-h-0">
        <p className="text-body text-muted mb-4">
          <Trans>Adds this trace as a single test case to the selected suite.</Trans>
        </p>

        {/* Body — two columns */}
        <div className="flex min-h-0 h-[min(600px,64vh)] rounded-lg overflow-hidden bg-card shadow-[inset_0_0_0_1px_var(--border-color)]">
          {/* Left: preview */}
          <div className="flex-1 min-w-0 border-r border-hairline overflow-y-auto px-6 py-5 flex flex-col gap-4">
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-caption font-semibold text-secondary uppercase tracking-[0.08em]">
                  <Trans>Input · <Plural value={inputMessages.length} one="# message" other="# messages" /></Trans>
                </span>
                {hasSystem && (
                  <span className="text-caption text-muted italic"><Trans>System messages excluded</Trans></span>
                )}
              </div>
              {inputMessages.length === 0 ? (
                <div className="px-3 py-4 bg-card-2 rounded-md text-body text-muted text-center">
                  <Trans>No input messages.</Trans>
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  {inputMessages.map((msg, i) => (
                    <MessageBubble key={i} msg={msg} defaultOpen />
                  ))}
                </div>
              )}
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-caption font-semibold text-secondary uppercase tracking-[0.08em]">
                  <Trans>Expected output</Trans>
                </span>
                <span className="text-caption text-muted italic"><Trans>Editable</Trans></span>
              </div>
              <ExpectedOutputEditor value={expected} tools={trace.tools} onChange={setExpected} />
            </div>
          </div>

          {/* Right: suite picker + stats */}
          <div className="w-[360px] shrink-0 flex flex-col min-h-0">
            <div className="px-5 pt-5 pb-3 shrink-0">
              <div className="text-caption font-semibold text-secondary uppercase tracking-[0.08em]">
                <Trans>Destination suite</Trans>
              </div>
              <div className="text-body-sm text-muted mt-0.5">
                <Plural value={suites.length} one="# suite for this agent" other="# suites for this agent" />
              </div>
            </div>

            <div className="flex-1 min-h-0 px-5 pb-3 flex flex-col">
              <SuitePicker suites={suites} value={suiteId} onChange={setSuiteId} />
            </div>

            {/* Stats panel */}
            <div className="px-5 py-4 border-t border-hairline shrink-0 bg-surface">
              {selectedSuite ? (
                <SuiteStats suite={selectedSuite} />
              ) : (
                <div className="text-body text-muted text-center py-2"><Trans>Select a suite</Trans></div>
              )}
            </div>
          </div>
        </div>
      </div>
    </Modal>
  );
}

function SuiteStats({ suite }: { suite: TestSuiteListItemDto }) {
  const { t } = useLingui();
  const passRateLabel = suite.passRate != null ? fmtPct100(suite.passRate) : '—';
  const lastRunLabel = suite.lastRunAt ? fmtRelative(suite.lastRunAt) : t`never`;

  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-3 gap-2">
        <Stat label={t`Cases`} value={String(suite.testCaseCount)} accent="var(--accent-primary)" />
        <Stat label={t`Pass rate`} value={passRateLabel} accent="var(--success)" />
        <Stat label={t`Total runs`} value={String(suite.totalRuns)} accent="var(--teal)" />
      </div>
      <div>
        <div className="text-caption font-semibold text-secondary uppercase tracking-[0.08em] mb-1.5">
          <Trans>Evaluators</Trans>
        </div>
        {suite.evaluators.length === 0 ? (
          <div className="text-body-sm text-muted italic"><Trans>None configured</Trans></div>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {suite.evaluators.map(e => (
              <ColoredBadge key={e.id} color={EVALUATOR_KIND_COLOR[e.kind]} label={e.kind} size="sm" />
            ))}
          </div>
        )}
      </div>
      <div className="flex items-center justify-between text-caption text-muted">
        <span><Trans>Last run</Trans></span>
        <span className="text-secondary">{lastRunLabel}</span>
      </div>
    </div>
  );
}

function Stat({ label, value, accent }: { label: string; value: string; accent: string }) {
  return (
    <div className="bg-card rounded-md px-2 py-2 text-center shadow-[inset_0_0_0_1px_var(--border-color)]">
      <div className="text-h1 font-bold font-mono" style={{ color: accent }}>{value}</div>
      <div className="text-caption text-secondary uppercase tracking-[0.06em] mt-0.5">{label}</div>
    </div>
  );
}
