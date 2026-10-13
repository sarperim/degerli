import {
  AdjustedNotice,
  AsOfDate,
  NoDataState,
  RestatedNotice,
  StaleBadge,
} from '@/components/honest-data'
import type { DataCoverage, DataState } from '@/lib/api/contracts'
import { cn } from '@/lib/utils'

export interface SectionMarkersProps {
  /** Data-as-of date for the section (UXR-G-007). */
  asOf: string
  /** Last-known-good data served past its freshness window (UXR-G-008). */
  stale?: boolean
  /** The section contains restated figures → footnote + warning (UXR-G-009). */
  restated?: boolean
  /** The section carries a corporate-action-adjusted series (UXR-G-010). */
  adjusted?: boolean
  /** When set, the section has no data → explicit state + coverage (UXR-G-006). */
  state?: DataState
  /** Coverage boundary accompanying a missing-data state. */
  coverage?: DataCoverage | null
  className?: string
}

/**
 * Honest-data marker strip for a stock-page section (SCR-005; UXR-RES-019/020/021/022).
 *
 * Composes the design-system marker vocabulary (TKT-foundation-011) into one
 * decision the sections share: a populated section shows its as-of date, a stale
 * marker when last-known-good, a restatement footnote + warning when any figure
 * was restated, and an adjusted-data disclaimer when a series is adjusted; a
 * missing section shows the explicit no-data/preparing/unavailable state with its
 * coverage boundary instead — never blank-as-zero (BR-RES-008).
 */
export function SectionMarkers({
  asOf,
  stale = false,
  restated = false,
  adjusted = false,
  state,
  coverage,
  className,
}: SectionMarkersProps) {
  if (state !== undefined) {
    return (
      <NoDataState
        state={state}
        availableFrom={coverage?.availableFrom ?? undefined}
        boundaryNote={coverage?.boundaryNote ?? undefined}
        className={className}
      />
    )
  }

  return (
    <div
      data-slot="section-markers"
      className={cn('flex flex-col gap-2', className)}
    >
      <div className="flex flex-wrap items-center gap-3">
        <AsOfDate asOf={asOf} />
        {stale ? <StaleBadge /> : null}
      </div>
      {restated ? <RestatedNotice /> : null}
      {adjusted ? <AdjustedNotice /> : null}
    </div>
  )
}
