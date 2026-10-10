import * as React from 'react'

import { cn } from '@/lib/utils'

export interface EmptyStateProps extends React.ComponentProps<'div'> {
  /** Optional leading icon (e.g. a lucide icon element). */
  icon?: React.ReactNode
  /** Short, plain-language headline naming the condition (UXR-G-005/006). */
  title: string
  /** Optional explanatory copy, coverage boundary, or suggested next step. */
  description?: string
  /** Optional action (button/link) suggesting a next step where one exists. */
  action?: React.ReactNode
}

/**
 * Empty / no-data state (design-system primitive, TKT-foundation-011).
 *
 * The explicit-empty surface required for every list and result area
 * (UXR-G-005) and for missing data with its coverage boundary (UXR-G-006):
 * values are never shown as blank-treated-as-zero. The `NoDataState` honest-data
 * component composes this primitive.
 */
export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
  ...props
}: EmptyStateProps) {
  return (
    <div
      data-slot="empty-state"
      className={cn(
        'flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed px-6 py-10 text-center',
        className,
      )}
      {...props}
    >
      {icon ? (
        <div className="text-muted-foreground [&_svg]:size-6" aria-hidden="true">
          {icon}
        </div>
      ) : null}
      <p className="text-h3 font-medium text-foreground">{title}</p>
      {description ? (
        <p className="max-w-prose text-sm text-muted-foreground">{description}</p>
      ) : null}
      {action ? <div className="pt-1">{action}</div> : null}
    </div>
  )
}
