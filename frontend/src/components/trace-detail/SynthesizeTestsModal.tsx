import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { plural } from '@lingui/core/macro';
import {
  type AgentCallDto,
  type EvaluatorSuggestionTarget,
  type TestSuiteListItemDto,
} from '../../api/models';
import useToast from '../../hooks/useToast';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useFeature, useLicense } from '../../hooks/useLicense';
import { Modal } from '../overlays/Modal';
import { Button } from '../ui/Button';
import { SkeletonList } from '../ui/Skeleton';
import { EYEBROW_CLS } from '../ui/classes';
import { SuitePicker } from './SuitePicker';
import { MAX_ROUNDS, useSynthesizeTests } from './useSynthesizeTests';
import { TranscriptPane } from './synthesis/TranscriptPane';
import { ProposalsPane } from './synthesis/ProposalsPane';
import { InstructionBar } from './synthesis/InstructionBar';
import { useProposalSelection } from './synthesis/useProposalSelection';
import type { JudgeChoice } from './synthesis/EvaluatorSuggestionCard';

interface Props {
  trace: AgentCallDto;
  suites: TestSuiteListItemDto[];
  onClose: () => void;
}

/**
 * Review panel for agent-proposed test cases: the conversation on the left, the ranked candidates
 * on the right, a free-text instruction at the bottom.
 *
 * Generation starts on open. "Generate tests" in the trace header *is* the generate action, so the
 * click that opens this panel is the click that spends the tokens — making the user click a second
 * time inside an empty panel bought nothing. Re-opening the panel therefore generates again.
 */
export function SynthesizeTestsModal({ trace, suites, onClose }: Props) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const { show: toast } = useToast();
  const { currentProjectId } = useCurrentProject();
  const [suiteId, setSuiteId] = useState(suites[0]?.id ?? '');
  const [instruction, setInstruction] = useState('');
  const [highlightedCallId, setHighlightedCallId] = useState<string | null>(null);
  const [judge, setJudge] = useState<JudgeChoice>({ target: 'none', newSuiteName: '' });

  const synthesis = useSynthesizeTests(trace);
  const { conversation, proposals, roundsUsed, generate, approve, abort } = synthesis;
  const selection = useProposalSelection(new Map(conversation.map(call => [call.id, call])));

  const canJudge = useFeature('AgenticEvaluators');
  const { data: license } = useLicense();
  const selectedSuite = suites.find(suite => suite.id === suiteId) ?? null;
  // On Free, MaxTestSuites is 1 and a project holds a single agent — so "the agent's suites" IS
  // the project's suites and this is exact. On Enterprise the limit is unbounded and never trips.
  const suiteLimitReached = suites.length >= (license?.limits.MaxTestSuites ?? Number.POSITIVE_INFINITY);

  // Synchronizing with something outside React — an in-flight fetch. Closing the panel must not
  // leave a generation running against the user's budget.
  useEffect(() => abort, [abort]);

  const busy = generate.isPending;
  const selectedCount = selection.checked.size;

  function runGenerate() {
    generate.mutate(
      { suiteId, instruction, current: proposals ? selection.withEdits(proposals) : null },
      {
        onSuccess: result => {
          selection.seed(result);
          setInstruction('');
          // Default to what the agent chose, so its recommendation is one click, not two.
          setJudge({
            target: result.evaluatorSuggestion?.target ?? 'none',
            newSuiteName: result.evaluatorSuggestion?.name ?? '',
          });
        },
      },
    );
  }

  // Synchronizing with something outside React — the generation request. The header button opens
  // this panel *in order to* generate, so it fires once on mount.
  //
  // It is scheduled rather than called inline, and guarded by its own cleanup rather than by a ref,
  // because a mutation started during the FIRST of StrictMode's two mount passes is orphaned:
  // tearing the effects down between the passes unsubscribes `useMutation`'s observer, and
  // `MutationObserver.onUnsubscribe` removes it from the mutation that is still in flight — with no
  // matching re-attach when it re-subscribes. The request then completes normally (the mutation's
  // own `onSuccess` still lands the proposals) but no observer ever hears the status change, so
  // `isPending` stays true and the panel shows its loading state forever, over a response it
  // already has. A ref guard makes that worse, not better: it suppresses the second pass's call,
  // which is the one whose observer would have survived.
  //
  // Deferring by a tick and clearing on cleanup means the first pass's timer is cancelled and only
  // the final mount's call runs — exactly one round, with an observer that stays attached.
  useEffect(() => {
    const timer = setTimeout(runGenerate, 0);
    return () => clearTimeout(timer);
    // Mount-only on purpose: re-running when the suite or instruction changes would generate behind
    // the user's back. Refinement is explicit, via the instruction bar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /** Sends the user to where the cases actually landed — a toast that only says "done" is a dead end. */
  function viewSuiteAction() {
    return { label: t`View suite`, onClick: () => navigate(`/suites?id=${suiteId}`) };
  }

  function submit() {
    if (!proposals) return;
    const writes = selection.writes(proposals.proposals);
    const suggestion = proposals.evaluatorSuggestion;
    const useJudge = canJudge && suggestion !== null && judge.target !== 'none';
    approve.mutate({
      suiteId,
      agentId: trace.agentId ?? '',
      projectId: currentProjectId ?? '',
      writes,
      judge: useJudge && suggestion
        ? {
          name: suggestion.name,
          instructions: suggestion.instructions,
          target: judge.target as EvaluatorSuggestionTarget,
        }
        : null,
      currentEvaluatorIds: selectedSuite?.evaluators.map(evaluator => evaluator.id) ?? [],
      newSuiteName: judge.newSuiteName.trim() || suggestion?.name || t`Generated suite`,
    }, {
      onSuccess: added => {
        toast(
          plural(added, { one: 'Added # test case', other: 'Added # test cases' }),
          // eslint-disable-next-line lingui/no-unlocalized-strings -- toast tone token, not UI copy
          'success',
          { action: viewSuiteAction() },
        );
        onClose();
      },
      onError: () => {
        // The writes are sequential, so a mid-way failure leaves the earlier ones applied — say how
        // many landed rather than implying nothing did, and still offer the suite if any did.
        const added = synthesis.addedBeforeFailure.current;
        toast(
          t`Added ${added} of ${writes.length} — the rest failed.`,
          // eslint-disable-next-line lingui/no-unlocalized-strings -- toast tone token, not UI copy
          'error',
          added > 0 ? { action: viewSuiteAction() } : undefined,
        );
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

            <ProposalsPane
              proposals={proposals}
              busy={busy}
              errorMessage={generate.isError ? (generate.error as Error).message : null}
              onGenerate={runGenerate}
              selection={selection}
              judge={{
                choice: judge,
                onChange: setJudge,
                licensed: canJudge,
                destination: {
                  // Named, not "this suite": the options say what happens to a suite the user can
                  // see by name at the top of this same column.
                  name: selectedSuite?.name ?? t`the suite`,
                  caseCount: selectedSuite?.testCaseCount ?? 0,
                  limitReached: suiteLimitReached,
                },
              }}
              onFocusCall={setHighlightedCallId}
            />

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
