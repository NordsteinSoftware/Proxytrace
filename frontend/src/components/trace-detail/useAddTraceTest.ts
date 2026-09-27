import { useMutation, useQueryClient } from '@tanstack/react-query';
import { testSuitesApi } from '../../api/test-suites';
import { QUERY_KEYS } from '../../api/query-keys';
import type { TestSuiteMessageDto } from '../../api/models';

interface AddTraceTestArgs {
  traceId: string;
  agentId: string;
  suiteId: string;
  newSuiteName: string;
  expectedOutput?: TestSuiteMessageDto;
}

/** Create the suite with its first case in one request, or append to an existing suite. */
export function useAddTraceTest() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ traceId, agentId, suiteId, newSuiteName, expectedOutput }: AddTraceTestArgs) =>
      suiteId
        ? testSuitesApi.addTestCase(suiteId, traceId, expectedOutput)
        : testSuitesApi.createWithCases({
          name: newSuiteName.trim(), agentId,
          testCases: [{ fromAgentCallId: traceId, expectedOutput }],
        }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: QUERY_KEYS.testSuitesRoot });
      // Creating a suite without an evaluator selection also creates its default evaluator.
      void qc.invalidateQueries({ queryKey: QUERY_KEYS.evaluatorsRoot });
    },
  });
}
