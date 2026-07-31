import { useRef } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { evaluatorsApi } from '../../api/evaluators';
import { testSuitesApi, type CreateTestCasePayload } from '../../api/test-suites';
import { QUERY_KEYS } from '../../api/query-keys';
import { EvaluatorKind, EvaluatorSuggestionTarget } from '../../api/models';

export interface ApproveArgs {
  suiteId: string;
  agentId: string;
  projectId: string;
  writes: CreateTestCasePayload[];
  /** Null when the agent suggested no judge, or the user declined it. */
  judge: { name: string; instructions: string; target: EvaluatorSuggestionTarget } | null;
  /** The destination suite's current evaluator ids — the update REPLACES the set. */
  currentEvaluatorIds: string[];
  newSuiteName: string;
}

/**
 * Writes the approved proposals. Three paths, in order of how much they disturb:
 *
 * 1. no judge — add each case to the destination suite;
 * 2. judge attached — create it, widen the suite's evaluator set, then add the cases;
 * 3. judge in a new suite — one call creates suite, cases and judge together.
 *
 * `addedBeforeFailure` exists because the per-case path is sequential: a mid-way failure leaves
 * the earlier writes applied, and the user deserves "added K of N" rather than a bare error.
 */
export function useApproveProposals() {
  const queryClient = useQueryClient();
  const added = useRef(0);

  const approve = useMutation({
    mutationFn: async (args: ApproveArgs) => {
      added.current = 0;

      const evaluatorId = args.judge
        ? (await evaluatorsApi.create({
          kind: EvaluatorKind.Agentic,
          projectId: args.projectId,
          name: args.judge.name,
          systemMessage: args.judge.instructions,
        })).id
        : null;

      if (evaluatorId && args.judge?.target === EvaluatorSuggestionTarget.NewSuite) {
        // One call creates the suite, all its cases and the judge — no partial state to explain.
        await testSuitesApi.createWithCases({
          name: args.newSuiteName,
          agentId: args.agentId,
          testCases: args.writes,
          evaluatorIds: [evaluatorId],
        });
        added.current = args.writes.length;
        return added.current;
      }

      if (evaluatorId) {
        // updateEvaluators REPLACES the set, so send the current ids plus the new one.
        await testSuitesApi.updateEvaluators(args.suiteId, [...args.currentEvaluatorIds, evaluatorId]);
      }

      for (const write of args.writes) {
        await testSuitesApi.addTestCase(args.suiteId, write.fromAgentCallId, write.expectedOutput);
        added.current += 1;
      }
      return added.current;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: QUERY_KEYS.testSuitesRoot });
      void queryClient.invalidateQueries({ queryKey: QUERY_KEYS.evaluatorsRoot });
    },
  });

  return { approve, addedBeforeFailure: added };
}
