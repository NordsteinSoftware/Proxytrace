import React from 'react';
import { cn } from '../../lib/cn';
import { fieldFocusWithinCls, formInputCls } from './classes';

type Size = 'sm' | 'md';

interface InputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'size' | 'prefix'> {
  inputSize?: Size;
  invalid?: boolean;
  leftAddon?: React.ReactNode;
  rightAddon?: React.ReactNode;
}

const SIZE_CLS: Record<Size, string> = {
  sm: cn('px-2.5 py-1.5 text-body-sm'),
  md: cn('px-3 py-2 text-title'),
};

export const Input = React.forwardRef<HTMLInputElement, InputProps>(function Input(
  // eslint-disable-next-line lingui/no-unlocalized-strings -- size variant token, not UI copy
  { inputSize = 'md', invalid, leftAddon, rightAddon, className, ...rest },
  ref,
) {
  if (leftAddon || rightAddon) {
    return (
      <div
        className={cn(
          'relative flex items-center w-full bg-card-2 border border-border-control rounded-md',
          'transition-[border-color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-standard)]',
          fieldFocusWithinCls,
          invalid && 'border-danger focus-within:border-danger focus-within:ring-danger',
        )}
      >
        {leftAddon && <span className="pl-3 text-secondary flex shrink-0 items-center text-body-sm">{leftAddon}</span>}
        <input
          ref={ref}
          data-invalid={invalid || undefined}
          className={cn(
            'min-w-0 flex-1 bg-transparent border-none outline-none text-primary font-[inherit]',
            SIZE_CLS[inputSize],
            className,
          )}
          {...rest}
        />
        {rightAddon && <span className="pr-3 text-secondary flex shrink-0 items-center text-body-sm">{rightAddon}</span>}
      </div>
    );
  }
  return (
    <input
      ref={ref}
      data-invalid={invalid || undefined}
      className={cn(formInputCls, SIZE_CLS[inputSize], className)}
      {...rest}
    />
  );
});
