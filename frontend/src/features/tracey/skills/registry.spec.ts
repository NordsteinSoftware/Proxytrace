import { describe, expect, it } from 'vitest';
import { getSkill, listSkills, skillCatalog, TRACEY_SKILLS } from './registry';

describe('tracey skills registry', () => {
  it('parses the optimize-agent skill from its markdown front-matter', () => {
    const skill = getSkill('optimize-agent');
    expect(skill).toBeDefined();
    expect(skill?.name).toBe('optimize-agent');
    expect(skill?.description).toContain('A/B-test');
    expect(skill?.instructions).toContain('Optimize an agent');
  });

  it('strips the front-matter from the instructions body', () => {
    expect(getSkill('optimize-agent')?.instructions.startsWith('---')).toBe(false);
  });

  it('returns undefined for an unknown skill', () => {
    expect(getSkill('does-not-exist')).toBeUndefined();
  });

  it('parses the front-matter `tools:` list into the skill bundle', () => {
    expect(getSkill('review-proposals')?.tools).toEqual([
      'list_proposals',
      'get_proposal',
      'set_proposal_status',
    ]);
  });

  it('lets a skill bundle the reads it needs from other areas', () => {
    // optimize-agent gathers evidence with run/trace reads owned by other skills too.
    expect(getSkill('optimize-agent')?.tools).toContain('submit_optimization_theory');
    expect(getSkill('optimize-agent')?.tools).toContain('get_run');
  });

  it('registers test-driven-improvement with the tools its red/green loop needs', () => {
    const skill = getSkill('test-driven-improvement');
    expect(skill?.tools).toEqual(expect.arrayContaining([
      'get_trace', 'list_suites', 'get_suite', 'create_suite', 'add_to_suite',
      'list_evaluators', 'create_evaluator', 'set_suite_evaluators',
      'start_test_run', 'get_case_results', 'compare_runs',
      'list_theories', 'submit_optimization_theory', 'await_actions',
    ]));
  });

  it('lists every loaded skill', () => {
    expect(listSkills()).toHaveLength(Object.keys(TRACEY_SKILLS).length);
    expect(listSkills().map((s) => s.name)).toContain('optimize-agent');
  });

  it('keys each skill by its front-matter name', () => {
    for (const [key, skill] of Object.entries(TRACEY_SKILLS)) {
      expect(key).toBe(skill.name);
    }
  });

  it('builds a one-line catalog entry per skill', () => {
    const lines = skillCatalog().split('\n');
    expect(lines).toHaveLength(listSkills().length);
    for (const skill of listSkills()) {
      expect(skillCatalog()).toContain(`\`${skill.name}\``);
      expect(skillCatalog()).toContain(skill.description);
    }
  });
});
