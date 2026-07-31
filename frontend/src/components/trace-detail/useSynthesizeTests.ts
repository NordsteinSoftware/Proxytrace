import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { agentCallsApi } from '../../api/agent-calls';
import { testSuitesApi, type CreateTestCasePayload } from '../../api/test-suites';
import { QUERY_KEYS } from '../../api/query-keys';
import type { AgentCallDto, SynthesisRoundDto, TestCaseProposalSetDto } from '../../api/models';

/** Refinement rounds carried into one request; the server enforces the same cap. */
export const MAX_ROUNDS = 5;

/** Upper bound on the calls fetched for one conversation, matching the server's own limit. */
const MAX_CONVERSATION_CALLS = 100;

interface GenerateArgs {
  suiteId: string;
  instruction: string;
  /** The proposals AS THE USER CURRENTLY HAS THEM, so a refinement revises what they see. */
  current: TestCaseProposalSetDto | null;
}

interface ApproveArgs {
  suiteId: string;
  writes: CreateTestCasePayload[];
}

/**
 * Owns the generate → refine → approve loop behind the Generate-tests panel.
 *
 * The rounds bookkeeping is the subtle part: `rounds` records what the model answered, and
 * `generate` replaces the LAST round's proposals with the user's current (possibly edited) set
 * before posting — so a follow-up request is made against what the user is looking at rather than
 * what the model originally said.
 */
export function useSynthesizeTests(trace: AgentCallDto) {
  const queryClient = useQueryClient();
  const [proposals, setProposals] = useState<TestCaseProposalSetDto | null>(null);
  const [rounds, setRounds] = useState<SynthesisRoundDto[]>([]);
  const added = useRef(0);
  // One controller per in-flight generation. Closing the panel aborts it, so a request the user
  // walked away from tears down its HTTP call instead of running on to completion on their budget.
  const inFlight = useRef<AbortController | null>(null);

  const conversationQuery = useQuery({
    queryKey: QUERY_KEYS.traceConversation(trace.conversationId),
    queryFn: () => agentCallsApi.listFull({
      conversationId: trace.conversationId ?? undefined,
      pageSize: MAX_CONVERSATION_CALLS,
      sortBy: 'CreatedAt',
      sortDesc: false,
    }),
    enabled: !!trace.conversationId,
  });

  // A trace with no conversation id is its own conversation of one.
  const conversation: AgentCallDto[] = trace.conversationId
    ? conversationQuery.data?.items ?? []
    : [trace];

  const generate = useMutation({
    mutationFn: ({ suiteId, instruction, current }: GenerateArgs) => {
      const history: SynthesisRoundDto[] = current && rounds.length > 0
        ? [...rounds.slice(0, -1), { ...rounds[rounds.length - 1], proposals: current }]
        : rounds;
      inFlight.current?.abort();
      const controller = new AbortController();
      inFlight.current = controller;
      return agentCallsApi.proposeTestCases(
        trace.id,
        {
          suiteId: suiteId || undefined,
          instruction: instruction.trim() || undefined,
          rounds: history.slice(-MAX_ROUNDS),
        },
        { signal: controller.signal },
      );
    },
    onSuccess: (result, variables) => {
      setProposals(result);
      setRounds(previous => [
        ...previous,
        { instruction: variables.instruction.trim() || null, proposals: result },
      ]);
    },
  });

  const approve = useMutation({
    mutationFn: async ({ suiteId, writes }: ApproveArgs) => {
      // Sequential, like useSaveSuite: on a mid-way failure the earlier writes stand, and `added`
      // is what lets the caller say "added K of N" instead of a bare failure.
      added.current = 0;
      for (const write of writes) {
        await testSuitesApi.addTestCase(suiteId, write.fromAgentCallId, write.expectedOutput);
        added.current += 1;
      }
      return added.current;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: QUERY_KEYS.testSuitesRoot });
    },
  });

  return {
    conversation,
    isLoadingConversation: !!trace.conversationId && conversationQuery.isLoading,
    proposals,
    roundsUsed: rounds.length,
    generate,
    approve,
    addedBeforeFailure: added,
    /** Called from the panel's unmount cleanup — synchronizing with an in-flight fetch. */
    abort: () => inFlight.current?.abort(),
  };
}
