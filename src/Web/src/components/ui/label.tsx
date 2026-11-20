import * as React from 'react'

import { cn } from '@/lib/utils'

/** Form label primitive, associated with its control via `htmlFor` (UXR-G-028). */
export function Label({ className, ...props }: React.ComponentProps<'label'>) {
  return (
    <label
      data-slot="label"
      className={cn(
        'flex items-center gap-1.5 font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground leading-none select-none',
        'peer-disabled:cursor-not-allowed peer-disabled:opacity-70',
        className,
      )}
      {...props}
    />
  )
}
