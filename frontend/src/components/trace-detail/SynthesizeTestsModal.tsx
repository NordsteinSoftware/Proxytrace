import { useEffect, useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { plural } from '@lingui/core/macro';
import type { AgentCallDto, TestSuiteListItemDto } from '../../api/models';
import useToast from '../../hooks/useToast';
import { Modal } from '../overlays/Modal';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { SkeletonList } from '../ui/Skeleton';
import { EYEBROW_CLS } from '../ui/classes';
import { SparklesIcon } from '../icons';
import { SuitePicker } from './SuitePicker';
import { MAX_ROUNDS, useSynthesizeTests } from './useSynthesizeTests';
import { TranscriptPane } from './synthesis/TranscriptPane';
import { ProposalList } from './synthesis/ProposalList';
import { SkippedTurns } from './synthesis/SkippedTurns';
import { InstructionBar } from './synthesis/InstructionBar';
import { useProposalSelection } from './synthesis/useProposalSelection';

interface Props {
  trace: AgentCallDto;
  suites: TestSuiteListItemDto[];
  onClose: () => void;
}

/**
 * Review panel for agent-proposed test cases: the conversation on the left, the ranked candidates
 * on the right, a free-text instruction at the bottom. Generation never runs on open — opening a
 * modal must not spend tokens on the project's system endpoint.
 */
export function SynthesizeTestsModal({ trace, suites, onClose }: Props) {
  const { t } = useLingui();
  const { show: toast } = useToast();
  const [suiteId, setSuiteId] = useState(suites[0]?.id ?? '');
  const [instruction, setInstruction] = useState('');
  const [highlightedCallId, setHighlightedCallId] = useState<string | null>(null);

  const synthesis = useSynthesizeTests(trace);
  const { conversation, proposals, roundsUsed, generate, approve, abort } = synthesis;
  const callById = new Map(conversation.map(call => [call.id, call]));
  const selection = useProposalSelection(callById);

  // Synchronizing with something outside React — an in-flight fetch. Closing the panel must not
  // leave a generation running against the user's budget.
  useEffect(() => abort, [abort]);

  const busy = generate.isPending;
  const selectedCount = selection.checked.size;
  const errorMessage = generate.isError ? (generate.error as Error).message : null;

  function runGenerate() {
    generate.mutate(
      { suiteId, instruction, current: proposals ? selection.withEdits(proposals) : null },
      { onSuccess: result => { selection.seed(result); setInstruction(''); } },
    );
  }

  function submit() {
    if (!proposals) return;
    const writes = selection.writes(proposals.proposals);
    approve.mutate({ suiteId, writes }, {
      onSuccess: added => {
        // eslint-disable-next-line lingui/no-unlocalized-strings -- toast tone token, not UI copy
        toast(plural(added, { one: 'Added # test case', other: 'Added # test cases' }), 'success');
        onClose();
      },
      onError: () => {
        // The writes are sequential, so a mid-way failure leaves the earlier ones applied — say how
        // many landed rather than implying nothing did.
        const added = synthesis.addedBeforeFailure.current;
        // eslint-disable-next-line lingui/no-unlocalized-strings -- toast tone token, not UI copy
        toast(t`Added ${added} of ${writes.length} — the rest failed.`, 'error');
      },
    });
  }

  return (
    <Modal
      title={t`Generate test cases`}
      onClose={onClose}
      size="xl"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}><Trans>Cancel</Trans></Button>
          <Button
            variant="primary"
            onClick={submit}
            disabled={selectedCount === 0 || !suiteId || approve.isPending}
            loading={approve.isPending}
            data-testid="synthesize-submit-btn"
          >
            <Trans>Add {selectedCount} to suite</Trans>
          </Button>
        </>
      }
    >
      <div data-testid="synthesize-tests-modal" className="flex flex-col min-h-0">
        <div className="flex min-h-0 h-[min(600px,64vh)] bg-card shadow-[var(--shadow-card)]">
          <div className="flex-1 min-w-0 border-r border-hairline overflow-y-auto px-5 py-4">
            {synthesis.isLoadingConversation
              ? <SkeletonList rows={4} />
              : <TranscriptPane calls={conversation} highlightedCallId={highlightedCallId} />}
          </div>

          <div className="w-[420px] shrink-0 flex flex-col min-h-0 px-5 py-4 gap-3">
            <div className="shrink-0 flex flex-col gap-1.5 max-h-[160px]">
              <span className={EYEBROW_CLS}><Trans>Destination suite</Trans></span>
              <SuitePicker suites={suites} value={suiteId} onChange={setSuiteId} />
            </div>

            <div className="flex-1 min-h-0 overflow-y-auto flex flex-col gap-2">
              {errorMessage && <p className="text-body-sm text-danger">{errorMessage}</p>}
              {busy && <SkeletonList rows={3} />}

              {!busy && !proposals && (
                <EmptyState
                  title={t`Nothing generated yet`}
                  description={t`Reads this trace's whole conversation and proposes the test cases worth building.`}
                  action={
                    <Button
                      variant="primary"
                      onClick={runGenerate}
                      leftIcon={<SparklesIcon size={13} />}
                      data-testid="synthesize-generate-btn"
                    >
                      <Trans>Generate</Trans>
                    </Button>
                  }
                />
              )}

              {!busy && proposals && proposals.proposals.length === 0 && (
                <EmptyState
                  title={t`No turn here is worth a test`}
                  description={proposals.summary || undefined}
                />
              )}

              {!busy && proposals && proposals.proposals.length > 0 && (
                <ProposalList
                  proposals={proposals.proposals}
                  callById={callById}
                  checked={selection.checked}
                  expectedFor={selection.expectedFor}
                  onToggle={selection.toggle}
                  onExpectedChange={selection.setExpected}
                  onFocus={setHighlightedCallId}
                />
              )}

              {!busy && proposals && <SkippedTurns skipped={proposals.skipped} />}
            </div>

            {proposals && (
              <InstructionBar
                value={instruction}
                onChange={setInstruction}
                onRegenerate={runGenerate}
                busy={busy}
                roundsUsed={roundsUsed}
                maxRounds={MAX_ROUNDS}
              />
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
}
