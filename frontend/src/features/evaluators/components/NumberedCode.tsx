import { cn } from '../../../lib/cn';
import type { TypeCategory } from '../evaluatorMeta';
import { categoryText, categoryVarHighlight } from '../categoryClasses';

const VAR_RE = /\{\{[a-z_]+\}\}/i;

interface Props {
  text: string;
  category: TypeCategory;
  highlightVars?: boolean;
}

/** Monospace code block with a gutter of line numbers and optional `{{var}}` highlighting. */
export function NumberedCode({ text, category, highlightVars = false }: Props) {
  const lines = text.split('\n');
  return (
    <div data-testid="evaluator-numbered-code" className="grid grid-cols-[36px_1fr] font-mono text-body-sm leading-[1.65]">
      {lines.map((ln, i) => (
        <div key={i} className="contents">
          <span className="text-muted text-right pr-3 text-caption select-none">{i + 1}</span>
          <span className="text-secondary whitespace-pre-wrap break-words">
            {highlightVars
              ? ln.split(/(\{\{[a-z_]+\}\})/gi).map((part, j) =>
                  VAR_RE.test(part)
                    ? (
                      <span key={j} className={cn('px-1 rounded-none', categoryVarHighlight[category], categoryText[category])}>
                        {part}
                      </span>
                    )
                    : <span key={j}>{part}</span>,
                )
              : (ln || ' ')}
          </span>
        </div>
      ))}
    </div>
  );
}
