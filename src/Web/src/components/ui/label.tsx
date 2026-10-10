import * as React from 'react'

import { cn } from '@/lib/utils'

/** Form label primitive, associated with its control via `htmlFor` (UXR-G-028). */
export function Label({ className, ...props }: React.ComponentProps<'label'>) {
  return (
    <label
      data-slot="label"
      className={cn(
        'flex items-center gap-2 text-sm font-medium leading-none select-none',
        'peer-disabled:cursor-not-allowed peer-disabled:opacity-70',
        className,
      )}
      {...props}
    />
  )
}
