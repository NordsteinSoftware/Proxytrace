import { describe, it, expect, vi, beforeEach } from 'vitest';
import { EvaluatorKind } from '../../../api/models';

const { evaluatorsApi } = vi.hoisted(() => ({
  evaluatorsApi: { list: vi.fn(), create: vi.fn() },
}));
vi.mock('../../../api/evaluators', () => ({ evaluatorsApi }));

import { createEvaluatorTools } from './evaluators';
import { CANCELLED } from './shared';
import type { TraceyTool, TraceyToolContext } from './shared';

// store echoes the digest back so the returned result is assertable. Async to satisfy StoreFn.
const store = vi.fn(async (_kind: string, _full: unknown, summary: unknown) => summary);

// `execute` is optional on the tool type, but every tool under test defines it.
function run(t: TraceyTool, args: Record<string, unknown>, ctx: TraceyToolContext) {
  if (!t.execute) throw new Error('tool has no execute');
  return t.execute(args, ctx);
}

function makeCtx(confirmValue = true): TraceyToolContext {
  return {
    projectId: 'p1',
    artifactScope: 'u:p',
    navigate: vi.fn(),
    confirm: vi.fn().mockResolvedValue(confirmValue),
    loadedSkillIds: new Set<string>(),
  };
}

beforeEach(() => vi.clearAllMocks());

describe('list_evaluators', () => {
  it("carries a clipped system message so a judge can be matched to a behavior", async () => {
    const ctx = makeCtx();
    evaluatorsApi.list.mockResolvedValue([
      {
        id: 'e1', kind: EvaluatorKind.Agentic, name: 'Policy judge',
        systemMessage: 'Fail responses that approve a refund outside the return window.',
      },
      { id: 'e2', kind: EvaluatorKind.ExactMatch, name: 'Exact match', systemMessage: null },
    ]);

    const result = await run(createEvaluatorTools(ctx, store).list_evaluators, {}, ctx) as {
      items: Record<string, unknown>[];
    };

    expect(result.items[0]).toMatchObject({
      id: 'e1',
      systemMessage: 'Fail responses that approve a refund outside the return window.',
    });
    expect(result.items[1]).not.toHaveProperty('systemMessage');
  });

  it('lists the project evaluators and returns a compact digest', async () => {
    const ctx = makeCtx();
    evaluatorsApi.list.mockResolvedValue([
      { id: 'e1', kind: EvaluatorKind.Agentic, name: 'Helpfulness' },
      { id: 'e2', kind: EvaluatorKind.ExactMatch, name: 'Exact match' },
    ]);

    const tool = createEvaluatorTools(ctx, store).list_evaluators;
    const result = await run(tool, {}, ctx);

    expect(evaluatorsApi.list).toHaveBeenCalledWith({ projectId: 'p1' });
    expect(store).toHaveBeenCalledWith('evaluator-list', expect.any(Array), expect.anything());
    expect(result).toEqual({
      count: 2,
      items: [
        { id: 'e1', kind: EvaluatorKind.Agentic, name: 'Helpfulness' },
        { id: 'e2', kind: EvaluatorKind.ExactMatch, name: 'Exact match' },
      ],
    });
  });
});

describe('create_evaluator', () => {
  const agenticDetails = {
    kind: EvaluatorKind.Agentic, name: 'Brevity judge', systemMessage: 'Fail bloated responses.',
  };

  it('confirms, creates with the project id, and returns the new evaluator identity', async () => {
    const ctx = makeCtx();
    evaluatorsApi.create.mockResolvedValue({ id: 'e9', kind: EvaluatorKind.Agentic, name: 'Brevity judge' });

    const tool = createEvaluatorTools(ctx, store).create_evaluator;
    const result = await run(tool, { details: agenticDetails }, ctx);

    expect(ctx.confirm).toHaveBeenCalledOnce();
    expect(evaluatorsApi.create).toHaveBeenCalledWith({ ...agenticDetails, projectId: 'p1' });
    expect(result).toEqual({ id: 'e9', kind: EvaluatorKind.Agentic, name: 'Brevity judge' });
  });

  it('returns CANCELLED on decline and never creates', async () => {
    const ctx = makeCtx(false);
    const tool = createEvaluatorTools(ctx, store).create_evaluator;
    const result = await run(tool, { details: agenticDetails }, ctx);
    expect(result).toBe(CANCELLED);
    expect(evaluatorsApi.create).not.toHaveBeenCalled();
  });

  it('maps a 402 (unlicensed agentic) to a notLicensed outcome instead of throwing', async () => {
    const ctx = makeCtx();
    evaluatorsApi.create.mockRejectedValue(Object.assign(new Error('payment required'), { status: 402 }));

    const tool = createEvaluatorTools(ctx, store).create_evaluator;
    const result = await run(tool, { details: agenticDetails }, ctx) as { outcome: string };

    expect(result.outcome).toBe('notLicensed');
  });

  it('maps any other failure to a plain error outcome', async () => {
    const ctx = makeCtx();
    evaluatorsApi.create.mockRejectedValue(Object.assign(new Error('boom'), { status: 500 }));

    const tool = createEvaluatorTools(ctx, store).create_evaluator;
    const result = await run(tool, { details: agenticDetails }, ctx) as { outcome: string; message: string };

    expect(result.outcome).toBe('error');
    expect(result.message).toBe('boom');
  });

  it('short-circuits without a project id and never confirms', async () => {
    const ctx: TraceyToolContext = { ...makeCtx(), projectId: undefined };
    const tool = createEvaluatorTools(ctx, store).create_evaluator;
    const result = await run(tool, { details: { kind: EvaluatorKind.ExactMatch } }, ctx);
    expect(result).toEqual({ outcome: 'noProject' });
    expect(ctx.confirm).not.toHaveBeenCalled();
    expect(evaluatorsApi.create).not.toHaveBeenCalled();
  });
});
