import * as React from 'react'

import { cn } from '@/lib/utils'

/**
 * Loading placeholders (design-system primitive, TKT-foundation-011).
 *
 * Used beneath the shell's route-level loading indicator to give per-surface
 * pending feedback (UXR-G-001/002) without shifting layout when content lands.
 * Skeletons are `aria-hidden`; the surrounding region owns the live `status`
 * announcement so screen readers are not spammed with placeholder nodes.
 */
export function Skeleton({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="skeleton"
      aria-hidden="true"
      className={cn('animate-pulse rounded-md bg-muted', className)}
      {...props}
    />
  )
}

/** A stack of text-line skeletons; the last line is short like real prose. */
export function SkeletonText({
  lines = 3,
  className,
}: {
  lines?: number
  className?: string
}) {
  return (
    <div data-slot="skeleton-text" aria-hidden="true" className={cn('space-y-2', className)}>
      {Array.from({ length: lines }).map((_, index) => (
        <Skeleton
          key={index}
          className={cn('h-4', index === lines - 1 ? 'w-2/3' : 'w-full')}
        />
      ))}
    </div>
  )
}

/** A table-shaped skeleton for data tables while their query resolves. */
export function SkeletonTable({
  rows = 5,
  columns = 4,
  className,
}: {
  rows?: number
  columns?: number
  className?: string
}) {
  return (
    <div
      data-slot="skeleton-table"
      aria-hidden="true"
      className={cn('w-full space-y-2 rounded-lg border p-3', className)}
    >
      {Array.from({ length: rows }).map((_, row) => (
        <div key={row} className="flex gap-3">
          {Array.from({ length: columns }).map((__, column) => (
            <Skeleton key={column} className="h-5 flex-1" />
          ))}
        </div>
      ))}
    </div>
  )
}
