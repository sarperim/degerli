import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, Info, SearchX } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { EmptyState } from '@/components/ui/empty-state'
import { formatAsOfDate } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Honest-data marker vocabulary (design-system component, TKT-foundation-011).
 *
 * The reusable display layer of the honest-data envelope (architecture §10.7):
 * every data-bearing surface renders its as-of date, stale data is marked,
 * restated figures carry an asterisk + footnote + warning, adjusted series carry
 * a disclaimer, and missing data is an explicit no-data state with its coverage
 * boundary — never blank-as-zero (UXR-G-006..010). Copy is localized; meaning is
 * always carried by text, not colour or icon alone (UXR-G-009).
 */

/** The three explicit missing-data states (architecture §10.7). */
export type DataState = 'no-data' | 'preparing' | 'unavailable'

/** Data-as-of date for any data-bearing display (UXR-G-007). */
export function AsOfDate({
  asOf,
  className,
}: {
  asOf: string
  className?: string
}) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  return (
    <span
      data-slot="as-of-date"
      className={cn('text-xs text-muted-foreground', className)}
    >
      {t('honestData.asOf', { date: formatAsOfDate(locale, asOf) })}
    </span>
  )
}

/** Marks last-known-good data served past its freshness window (UXR-G-008). */
export function StaleBadge({
  className,
  label,
}: {
  className?: string
  label?: string
}) {
  const { t } = useTranslation()
  return (
    <Badge data-slot="stale-badge" variant="stale" className={className}>
      <AlertTriangle aria-hidden="true" />
      {label ?? t('honestData.stale')}
    </Badge>
  )
}

/** Inline not-meaningful state for a metric with no meaningful value (UXR-RES-025). */
export function NotMeaningful({
  className,
  label,
}: {
  className?: string
  label?: string
}) {
  const { t } = useTranslation()
  const text = label ?? t('honestData.notMeaningful')
  return (
    <span
      data-slot="not-meaningful"
      title={t('honestData.notMeaningfulTitle')}
      className={cn(
        'rounded-md border border-border bg-not-meaningful px-2 py-0.5 text-xs text-not-meaningful-foreground',
        className,
      )}
    >
      {text}
    </span>
  )
}

/**
 * Restatement asterisk for an affected figure (UXR-G-009). The visible glyph is
 * a plain text asterisk; the full meaning is exposed to assistive tech via the
 * visually-hidden description and to sighted users via the footnote.
 */
export function RestatedMarker({
  className,
  description,
}: {
  className?: string
  description?: string
}) {
  const { t } = useTranslation()
  return (
    <span
      data-slot="restated-marker"
      className={cn('font-semibold text-restated-foreground', className)}
      title={description ?? t('honestData.restated')}
    >
      <span aria-hidden="true">*</span>
      <span className="sr-only"> ({description ?? t('honestData.restated')})</span>
    </span>
  )
}

/** Bottom-of-display restatement footnote + warning indicator (UXR-G-009). */
export function RestatedNotice({
  className,
  children,
}: {
  className?: string
  children?: ReactNode
}) {
  const { t } = useTranslation()
  return (
    <p
      data-slot="restated-notice"
      className={cn(
        'flex items-start gap-1.5 text-xs text-muted-foreground',
        className,
      )}
    >
      <AlertTriangle
        aria-hidden="true"
        className="mt-0.5 size-3 shrink-0 text-restated-foreground"
      />
      <span>
        <span aria-hidden="true" className="font-semibold">
          *
        </span>{' '}
        {children ?? t('honestData.restatedNotice')}
      </span>
    </p>
  )
}

/** Visible adjusted-data disclaimer for corporate-action-adjusted series (UXR-G-010). */
export function AdjustedNotice({
  className,
  children,
}: {
  className?: string
  children?: ReactNode
}) {
  const { t } = useTranslation()
  return (
    <p
      data-slot="adjusted-notice"
      className={cn(
        'flex items-start gap-1.5 rounded-md border border-adjusted-foreground/20 bg-adjusted/60 p-2 text-xs text-adjusted-foreground',
        className,
      )}
    >
      <Info aria-hidden="true" className="mt-0.5 size-3 shrink-0" />
      <span>{children ?? t('honestData.adjustedNotice')}</span>
    </p>
  )
}

const NO_DATA_TITLE_KEYS = {
  'no-data': 'honestData.noDataTitle',
  preparing: 'honestData.preparingTitle',
  unavailable: 'honestData.unavailableTitle',
} as const

/**
 * Explicit no-data / preparing / unavailable state with coverage boundary
 * (UXR-G-006; FR-RES-016/026). Composes `EmptyState`, so it is announced and
 * actionable like every other empty surface.
 */
export function NoDataState({
  state = 'no-data',
  availableFrom,
  boundaryNote,
  className,
}: {
  state?: DataState
  /** Coverage start date; rendered as the coverage boundary when present. */
  availableFrom?: string
  /** Additional coverage-boundary note (e.g. "listed in 2019"). */
  boundaryNote?: string
  className?: string
}) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const title = t(NO_DATA_TITLE_KEYS[state])

  const boundary = availableFrom
    ? t('honestData.coverageBoundary', {
        date: formatAsOfDate(locale, availableFrom),
      })
    : undefined
  const description =
    [boundary, boundaryNote].filter(Boolean).join(' ') ||
    (state === 'no-data' ? t('honestData.noDataBody') : undefined)

  return (
    <EmptyState
      data-state={state}
      icon={<SearchX />}
      title={title}
      description={description}
      className={className}
    />
  )
}
