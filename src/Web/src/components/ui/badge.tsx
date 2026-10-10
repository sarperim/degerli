import * as React from 'react'
import { cva, type VariantProps } from 'class-variance-authority'

import { cn } from '@/lib/utils'

const badgeVariants = cva(
  'inline-flex w-fit shrink-0 items-center justify-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-medium [&_svg]:pointer-events-none [&_svg]:size-3 [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        default: 'border-transparent bg-primary text-primary-foreground',
        secondary: 'border-transparent bg-secondary text-secondary-foreground',
        outline: 'text-foreground',
        destructive:
          'border-transparent bg-destructive text-destructive-foreground',
        /* Honest-data vocabulary (architecture §10.7; UXR-G-007..010). */
        stale: 'border-stale-foreground/20 bg-stale text-stale-foreground',
        restated:
          'border-restated-foreground/20 bg-restated text-restated-foreground',
        adjusted:
          'border-adjusted-foreground/20 bg-adjusted text-adjusted-foreground',
        notMeaningful:
          'border-border bg-not-meaningful text-not-meaningful-foreground',
        success:
          'border-success-foreground/20 bg-success text-success-foreground',
        warning:
          'border-warning-foreground/20 bg-warning text-warning-foreground',
        info: 'border-info-foreground/20 bg-info text-info-foreground',
      },
    },
    defaultVariants: {
      variant: 'default',
    },
  },
)

export interface BadgeProps
  extends React.ComponentProps<'span'>,
    VariantProps<typeof badgeVariants> {}

/** Small status/label pill (design-system primitive, TKT-foundation-011). */
export function Badge({ className, variant, ...props }: BadgeProps) {
  return (
    <span
      data-slot="badge"
      className={cn(badgeVariants({ variant }), className)}
      {...props}
    />
  )
}

export { badgeVariants }
