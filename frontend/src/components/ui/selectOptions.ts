import React from 'react';

/**
 * The `<option>`-children parser behind {@link Select}. Pure and framework-testable, in its own
 * module so the component file exports only components (Fast Refresh needs that).
 */

export interface OptionData {
  value: string;
  label: React.ReactNode;
  disabled?: boolean;
}

/** A run of options, headed by an `<optgroup>` label when they came from one. */
export interface OptionGroup {
  label?: string;
  options: OptionData[];
}

function isOptionElement(
  node: React.ReactNode,
): node is React.ReactElement<React.OptionHTMLAttributes<HTMLOptionElement>> {
  return React.isValidElement(node) && node.type === 'option';
}

function isOptGroupElement(
  node: React.ReactNode,
): node is React.ReactElement<React.OptgroupHTMLAttributes<HTMLOptGroupElement>> {
  return React.isValidElement(node) && node.type === 'optgroup';
}

function toOption(el: React.ReactElement<React.OptionHTMLAttributes<HTMLOptionElement>>): OptionData {
  const { value, children: label, disabled } = el.props;
  const resolvedValue = value !== undefined ? String(value) : typeof label === 'string' ? label : '';
  return { value: resolvedValue, label, disabled };
}

/**
 * Collects `<option>` children into renderable groups, descending into `<optgroup>` (labels stay
 * nodes). Ungrouped options keep their position as an unlabelled run, so a leading bare option
 * ("Whole project") still renders above the groups that follow it.
 *
 * Descending into `<optgroup>` matters more than it looks: this parser previously kept only direct
 * `<option>` children and dropped everything else **silently**, so a grouped call site rendered an
 * empty menu with no error anywhere — the options simply vanished.
 */
export function collectGroups(children: React.ReactNode): OptionGroup[] {
  const groups: OptionGroup[] = [];

  for (const node of React.Children.toArray(children)) {
    if (isOptionElement(node)) {
      // Extend the current ungrouped run rather than starting a new group per option.
      const last = groups[groups.length - 1];
      if (last && last.label === undefined) last.options.push(toOption(node));
      else groups.push({ options: [toOption(node)] });
      continue;
    }

    if (isOptGroupElement(node)) {
      const options = React.Children.toArray(node.props.children)
        .filter(isOptionElement)
        .map(toOption);
      if (options.length > 0) groups.push({ label: node.props.label, options });
    }
  }

  return groups;
}
