import { z } from 'zod';
import { msg } from '@lingui/core/macro';
import { evaluatorsApi } from '../../../api/evaluators';
import { EvaluatorKind } from '../../../api/models';
import { type ToolFactory, tool, CANCELLED, listDigest, presentArg } from './shared';
import { clip } from './run-analysis';

/** Kind-specific evaluator configuration accepted by `create_evaluator`. */
const evaluatorDetailsSchema = z.discriminatedUnion('kind', [
  z.object({
    kind: z.literal(EvaluatorKind.Agentic),
    name: z.string().min(1).describe('A short name for the judge, e.g. "Latency-safe brevity".'),
    systemMessage: z.string().min(1)
      .describe('The judge\'s system prompt: what to check in the response and how to score it.'),
  }),
  z.object({ kind: z.literal(EvaluatorKind.ExactMatch) }),
  z.object({
    kind: z.literal(EvaluatorKind.NumericMatch),
    extractionPattern: z.string().min(1).describe('Regex that extracts the number to compare from the response.'),
    tolerance: z.number().describe('Allowed absolute difference from the expected number.'),
  }),
  z.object({
    kind: z.literal(EvaluatorKind.JsonSchemaMatch),
    jsonSchema: z.string().min(1).describe('The JSON schema the response must validate against.'),
  }),
]);

export const createEvaluatorTools: ToolFactory = (ctx, store) => {
  const projectId = ctx.projectId;
  return {
    list_evaluators: tool({
      description:
        'List the project\'s evaluators — the scorers (LLM judge, exact/numeric/JSON-schema match) ' +
        'a test suite grades its cases with. Each agentic judge carries its `systemMessage`, so you ' +
        'can tell whether one already covers the behavior you need to score. Check this BEFORE ' +
        'create_evaluator: reuse a fitting one by passing its id to create_suite\'s evaluatorIds or ' +
        'to set_suite_evaluators. Rendered to the user as a card.',
      parameters: z.object({ present: presentArg }),
      confirm: false,
      execute: async () => {
        const items = await evaluatorsApi.list({ projectId });
        return store('evaluator-list', items, listDigest(items, 25, (e) => ({
          id: e.id,
          kind: e.kind,
          name: e.name,
          // The judge's instructions are what let the model decide whether an existing evaluator
          // already covers a behavior — id/kind/name cannot answer that.
          ...(e.systemMessage ? { systemMessage: clip(e.systemMessage, 300) } : {}),
        })));
      },
    }),
    create_evaluator: tool({
      description:
        'Create an evaluator to score test cases with. Requires confirmation. Kinds: Agentic (an ' +
        'LLM judge you give a focused system prompt — best for behavioral checks like tone, ' +
        'brevity, or a specific failure pattern; needs an enterprise license), ExactMatch, ' +
        'NumericMatch (regex + tolerance), JsonSchemaMatch. Attach the returned id to a suite via ' +
        'create_suite\'s evaluatorIds.',
      parameters: z.object({ details: evaluatorDetailsSchema }),
      confirm: true,
      execute: async ({ details }, c) => {
        if (!projectId) return { outcome: 'noProject' };
        const label = details.kind === EvaluatorKind.Agentic ? ` "${details.name}"` : '';
        const ok = await c.confirm(`Create a ${details.kind} evaluator${label}?`);
        if (!ok) return CANCELLED;
        try {
          const created = await evaluatorsApi.create({ ...details, projectId });
          return { id: created.id, kind: created.kind, name: created.name };
        } catch (error) {
          const status = (error as { status?: number }).status;
          if (status === 402) {
            return {
              outcome: 'notLicensed',
              message: msg`Agentic evaluators require a licensed installation. Use a non-agentic kind or the suite's default exact-match evaluator instead.`,
            };
          }
          return { outcome: 'error', message: error instanceof Error ? error.message : 'Failed to create the evaluator.' };
        }
      },
    }),
  };
};
