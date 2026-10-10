import * as React from 'react'

import { cn } from '@/lib/utils'

export interface InputProps extends React.ComponentProps<'input'> {
  /**
   * Marks the control as invalid for styling. `aria-invalid` is the source of
   * truth; passing `invalid` is a convenience so callers need not set both.
   */
  invalid?: boolean
}

/**
 * Text input (design-system primitive, TKT-foundation-011).
 *
 * Validation states (UXR-G-018): default, focused, invalid (destructive border +
 * ring), disabled. `aria-invalid` is always reflected so assistive tech sees the
 * state (UXR-G-026/028); pairing with `FormField` wires `aria-describedby` to
 * the inline message.
 */
export function Input({
  className,
  invalid,
  'aria-invalid': ariaInvalid,
  type = 'text',
  ...props
}: InputProps) {
  const isInvalid = invalid ?? (ariaInvalid === true || ariaInvalid === 'true')

  return (
    <input
      data-slot="input"
      type={type}
      aria-invalid={isInvalid ? true : undefined}
      className={cn(
        'h-9 w-full min-w-0 rounded-md border border-input bg-background px-3 py-1 text-sm shadow-xs outline-none transition-[color,box-shadow]',
        'placeholder:text-muted-foreground selection:bg-primary selection:text-primary-foreground',
        'focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]',
        'disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50',
        'aria-invalid:border-destructive aria-invalid:ring-destructive/20',
        'file:inline-flex file:border-0 file:bg-transparent file:text-sm file:font-medium',
        className,
      )}
      {...props}
    />
  )
}
