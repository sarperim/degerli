import type { ValuationHistoricalSeries } from '@/lib/api/contracts'

/**
 * Pure valuation-display helpers (SCR-005 §2, TKT-res-009).
 *
 * Kept out of the component module so the `.tsx` files export only React
 * components (react-refresh boundary) and the plotting/basis logic is unit
 * testable without a DOM or a chart library.
 */

/** The five valuation-family metrics the strip carries, in display order (OQ-UX-002). */
export const VALUATION_METRIC_ORDER = [
  'pe',
  'pb',
  'ev_ebitda',
  'ev_fcf',
  'fcf_yield',
] as const

export type ValuationMetricCode = (typeof VALUATION_METRIC_ORDER)[number]

/** Metrics carried as fractions (`0.05` → `5%`); the rest are raw ratios. */
const PERCENT_METRICS = new Set<string>(['fcf_yield'])

/** True when a valuation metric is a percentage stored as a fraction. */
export function isPercentValuationMetric(code: string): boolean {
  return PERCENT_METRICS.has(code)
}

/** One point handed to the chart. */
export interface ValuationHistoryPoint {
  date: string
  close: number
  /** True when this point is on the adjusted basis. */
  adjusted: boolean
}

/**
 * Reduce the served history to the points the chart plots.
 *
 * When the payload is on the adjusted basis (every row carries `closeAdjusted`,
 * `historical.adjusted === true`) the adjusted close is plotted — so a later
 * split/bonus never shows as a price cliff (BR-MDF-010, UXR-RES-020). When the
 * basis is raw, the raw close is plotted and the disclaimer is not claimed.
 */
export function valuationHistoryPoints(
  historical: ValuationHistoricalSeries,
): ValuationHistoryPoint[] {
  return historical.series.map((point) => ({
    date: point.date,
    close:
      historical.adjusted && point.closeAdjusted !== null
        ? point.closeAdjusted
        : point.closeRaw,
    adjusted: historical.adjusted && point.closeAdjusted !== null,
  }))
}
