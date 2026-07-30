import { describe, expect, it } from 'vitest';
import { collectGroups } from './selectOptions';

/**
 * `Select` keeps an `<option>`-children API but renders a Radix menu, so a parser turns those
 * children into menu items. It used to keep only *direct* `<option>` children and drop everything
 * else **silently** — so wrapping options in `<optgroup>` produced an empty menu with no error
 * anywhere, and the call site looked correct. These tests pin the grouped shape.
 */
describe('collectGroups', () => {
  it('collects bare options as one unlabelled run', () => {
    const groups = collectGroups([
      <option key="a" value="a">A</option>,
      <option key="b" value="b">B</option>,
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBeUndefined();
    expect(groups[0].options.map(o => o.value)).toEqual(['a', 'b']);
  });

  it('descends into optgroup and keeps its label', () => {
    const groups = collectGroups([
      <optgroup key="g" label="Agent">
        <option value="agent:1">Support bot</option>
        <option value="agent:2">Billing bot</option>
      </optgroup>,
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe('Agent');
    expect(groups[0].options.map(o => o.value)).toEqual(['agent:1', 'agent:2']);
  });

  it('keeps a leading bare option above the groups that follow it', () => {
    // The exact shape the budget scope picker renders: "Whole project", then Agent, then API Key.
    const groups = collectGroups([
      <option key="p" value="">Whole project</option>,
      <optgroup key="a" label="Agent"><option value="agent:1">Bot</option></optgroup>,
      <optgroup key="k" label="API Key"><option value="apiKey:1">CI</option></optgroup>,
    ]);

    expect(groups.map(g => g.label)).toEqual([undefined, 'Agent', 'API Key']);
    expect(groups.flatMap(g => g.options).map(o => o.value)).toEqual(['', 'agent:1', 'apiKey:1']);
  });

  it('drops an empty optgroup rather than rendering a heading with nothing under it', () => {
    const groups = collectGroups([
      <option key="p" value="">Whole project</option>,
      <optgroup key="a" label="Agent">{[]}</optgroup>,
    ]);

    expect(groups.map(g => g.label)).toEqual([undefined]);
  });

  it('ignores a falsy child from a conditional render', () => {
    // `{cond && <optgroup/>}` yields `false` when the list is empty — must not throw or emit a group.
    const groups = collectGroups([<option key="p" value="">Whole project</option>, false, null]);

    expect(groups).toHaveLength(1);
    expect(groups[0].options).toHaveLength(1);
  });

  it('carries the disabled flag through', () => {
    const groups = collectGroups([<option key="a" value="a" disabled>A</option>]);

    expect(groups[0].options[0].disabled).toBe(true);
  });
});
