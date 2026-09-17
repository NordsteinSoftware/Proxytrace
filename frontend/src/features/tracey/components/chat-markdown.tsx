import type { Components } from 'react-markdown';
import { clean, markdownComponents } from '../../../components/markdown/markdown-components';

/**
 * Chat-scale overrides of the shared markdown element map. Tracey's thread is a reading surface,
 * not a data grid, so prose renders at the shared reading tier (`text-reading`, 15px) and
 * headings/code step up with it; everything not overridden here inherits the
 * shared {@link markdownComponents} treatment.
 */
export const chatMarkdownComponents: Components = {
  ...markdownComponents,
  h1: (props) => (
    <h1 className="mb-2 mt-4 text-h1 font-semibold text-primary first:mt-0" {...clean(props)} />
  ),
  h2: (props) => (
    <h2 className="mb-2 mt-3.5 text-reading-title font-semibold text-primary first:mt-0" {...clean(props)} />
  ),
  h3: (props) => (
    <h3 className="mb-1.5 mt-3 text-reading font-semibold text-primary first:mt-0" {...clean(props)} />
  ),
  code: (props) => (
    <code
      className="rounded-sm bg-surface px-1 py-0.5 font-mono text-title text-accent"
      {...clean(props)}
    />
  ),
  pre: (props) => (
    <pre
      className="my-2.5 overflow-x-auto rounded-lg border border-border bg-surface p-3 text-title leading-relaxed text-primary [&_code]:bg-transparent [&_code]:p-0 [&_code]:text-primary"
      {...clean(props)}
    />
  ),
  table: (props) => (
    <div className="my-2.5 overflow-x-auto">
      <table className="w-full border-collapse text-title" {...clean(props)} />
    </div>
  ),
};
