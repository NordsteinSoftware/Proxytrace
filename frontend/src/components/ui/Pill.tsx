import { Badge } from './Badge';

interface PillProps {
  label: string;
  color: string;
  size?: 'sm' | 'md';
  onClick?: () => void;
  selected?: boolean;
}

/**
 * Entity-colored tag (model, agent, level, …). The name is historical: it renders a square,
 * tinted `Badge` — there is no pill shape left. Kept because ~15 call sites outside this
 * directory import it by name.
 */
export function Pill({ label, color, size = 'md', onClick, selected }: PillProps) {
  return (
    <Badge
      label={label}
      variant="tinted"
      color={color}
      size={size}
      onClick={onClick}
      selected={selected}
      // Long labels (model ids) truncate inside narrow row columns — keep the full value reachable.
      title={label}
    />
  );
}
