import * as React from 'react'

import { cn } from '@/lib/utils'

/**
 * Semantic table primitives (design-system primitive, TKT-foundation-011).
 *
 * Built on native `<table>` elements so assistive tech gets real table
 * semantics: caption, column headers with `scope`, row headers where relevant
 * (UXR-G-028, architecture M-8). Numeric cells should carry the `tabular-nums`
 * utility so digit columns align.
 *
 * Narrow widths (UXR-G-025): `TableContainer` scrolls horizontally instead of
 * dropping columns — every figure, marker and action stays reachable. Screens
 * may instead hide low-priority columns with responsive utilities, but the
 * foundation default loses no data.
 */
export function TableContainer({
  className,
  ...props
}: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="table-container"
      className={cn(
        'relative w-full overflow-x-auto rounded-[2px] border border-border',
        className,
      )}
      {...props}
    />
  )
}

export function Table({ className, ...props }: React.ComponentProps<'table'>) {
  return (
    <table
      data-slot="table"
      className={cn('w-full caption-bottom border-collapse text-sm', className)}
      {...props}
    />
  )
}

export function TableCaption({
  className,
  ...props
}: React.ComponentProps<'caption'>) {
  return (
    <caption
      data-slot="table-caption"
      className={cn('mt-3 px-3 text-left font-mono text-[11px] text-muted-foreground', className)}
      {...props}
    />
  )
}

export function TableHeader({
  className,
  ...props
}: React.ComponentProps<'thead'>) {
  return (
    <thead
      data-slot="table-header"
      className={cn('border-b border-[var(--border-strong)] bg-muted/40 [&_tr]:border-b', className)}
      {...props}
    />
  )
}

export function TableBody({
  className,
  ...props
}: React.ComponentProps<'tbody'>) {
  return (
    <tbody
      data-slot="table-body"
      className={cn('[&_tr:last-child]:border-0', className)}
      {...props}
    />
  )
}

export function TableFooter({
  className,
  ...props
}: React.ComponentProps<'tfoot'>) {
  return (
    <tfoot
      data-slot="table-footer"
      className={cn('border-t border-border bg-muted/40 font-medium', className)}
      {...props}
    />
  )
}

export function TableRow({ className, ...props }: React.ComponentProps<'tr'>) {
  return (
    <tr
      data-slot="table-row"
      className={cn(
        'border-b border-border transition-colors hover:bg-accent/50 data-[state=selected]:bg-muted',
        className,
      )}
      {...props}
    />
  )
}

export interface TableHeadProps extends React.ComponentProps<'th'> {
  /** `scope` is required for header cells in an accessible table. */
  scope?: 'col' | 'row' | 'colgroup' | 'rowgroup'
}

export function TableHead({ className, scope = 'col', ...props }: TableHeadProps) {
  return (
    <th
      data-slot="table-head"
      scope={scope}
      className={cn(
        'px-3 text-left align-middle',
        scope === 'row'
          ? 'py-2.5 whitespace-normal text-sm font-medium text-foreground'
          : 'h-9 whitespace-nowrap font-mono text-[10px] font-medium tracking-[0.06em] uppercase text-[var(--ink-muted)]',
        className,
      )}
      {...props}
    />
  )
}

export function TableCell({ className, ...props }: React.ComponentProps<'td'>) {
  return (
    <td
      data-slot="table-cell"
      className={cn('px-3 py-2.5 align-middle tabular-nums', className)}
      {...props}
    />
  )
}
