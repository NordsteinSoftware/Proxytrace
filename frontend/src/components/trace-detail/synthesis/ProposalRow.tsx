import { Trans, useLingui } from '@lingui/react/macro';
import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import {
  TestCaseProposalFlag,
  TestCaseProposalKind,
  TestCaseProposalRelevance,
  type AgentCallDto,
  type TestCaseProposalDto,
} from '../../../api/models';
import { Badge } from '../../ui/Badge';
import { Checkbox } from '../../ui/Checkbox';
import { ExpectedOutputEditor } from '../../expected-output/ExpectedOutputEditor';
import type { ExpectedOutput } from '../../expected-output/expectedOutput';

const RELEVANCE_LABELS: Record<TestCaseProposalRelevance, MessageDescriptor> = {
  [TestCaseProposalRelevance.High]: msg`high`,
  [TestCaseProposalRelevance.Medium]: msg`medium`,
  [TestCaseProposalRelevance.Low]: msg`low`,
};

interface Props {
  proposal: TestCaseProposalDto;
  call: AgentCallDto | undefined;
  checked: boolean;
  expected: ExpectedOutput;
  onToggle: () => void;
  onExpectedChange: (value: ExpectedOutput) => void;
  onFocus: () => void;
}

/**
 * One proposed test case: what it asserts, why it is worth testing, and — for a correction — the
 * editable expected output. A flagged proposal states its problem in place rather than silently
 * arriving unchecked.
 */
export function ProposalRow({
  proposal, call, checked, expected, onToggle, onExpectedChange, onFocus,
}: Props) {
  const { t, i18n } = useLingui();
  const isCorrection = proposal.kind === TestCaseProposalKind.Correction;
  const testId = `${proposal.agentCallId}-${proposal.kind}`;

  return (
    <div
      className="flex flex-col gap-2 p-3 bg-card-2 shadow-[inset_0_0_0_1px_var(--border-color)]"
      data-testid={`synthesis-proposal-${testId}`}
      onMouseEnter={onFocus}
    >
      <div className="flex items-start gap-2">
        <Checkbox
          checked={checked}
          onChange={onToggle}
          aria-label={proposal.title}
          data-testid={`synthesis-proposal-toggle-${testId}`}
        />
        <div className="flex-1 min-w-0 flex flex-col gap-1">
          <div className="flex flex-wrap items-center gap-1.5">
            <Badge
              label={isCorrection ? t`RED` : t`GREEN`}
              variant={isCorrection ? 'danger' : 'success'}
              size="sm"
            />
            <Badge label={i18n._(RELEVANCE_LABELS[proposal.relevance])} variant="neutral" size="sm" />
            <span className="text-body font-semibold text-primary truncate min-w-0">{proposal.title}</span>
          </div>
          <p className="text-body-sm text-secondary">{proposal.rationale}</p>
          {proposal.flags.includes(TestCaseProposalFlag.Unpassable) && (
            <p className="text-body-sm text-warn">
              <Trans>
                This turn's input already contains the tool calls and their results, so a corrected
                answer can never pass. Correct the earlier call that made the decision instead.
              </Trans>
            </p>
          )}
          {proposal.flags.includes(TestCaseProposalFlag.UnknownTool) && (
            <p className="text-body-sm text-warn">
              <Trans>The expected output calls a tool this agent was not offered.</Trans>
            </p>
          )}
        </div>
      </div>
      {isCorrection && (
        <ExpectedOutputEditor value={expected} tools={call?.tools ?? []} onChange={onExpectedChange} />
      )}
    </div>
  );
}
