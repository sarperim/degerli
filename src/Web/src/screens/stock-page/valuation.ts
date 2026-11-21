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

/** Supported interval granularities for the historical price chart (SLICE-032). */
export type ValuationChartGranularity = 'daily' | 'weekly' | 'monthly'

/** Display order for the historical price chart granularity selector. */
export const VALUATION_CHART_GRANULARITIES: readonly ValuationChartGranularity[] = [
  'daily',
  'weekly',
  'monthly',
]

const ISO_DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/

function periodBucketKey(
  date: string,
  granularity: Exclude<ValuationChartGranularity, 'daily'>,
): string {
  const match = ISO_DATE_RE.exec(date)
  if (!match) return date

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])

  if (granularity === 'monthly') {
    return `${match[1]}-${match[2]}`
  }

  const utc = new Date(Date.UTC(year, month - 1, day))
  if (
    utc.getUTCFullYear() !== year ||
    utc.getUTCMonth() !== month - 1 ||
    utc.getUTCDate() !== day
  ) {
    return date
  }

  // Shift to Thursday of the same ISO 8601 week (1 = Mon .. 7 = Sun).
  const dayNum = utc.getUTCDay() || 7
  utc.setUTCDate(utc.getUTCDate() + 4 - dayNum)
  const isoYear = utc.getUTCFullYear()
  const yearStart = Date.UTC(isoYear, 0, 1)
  const week = Math.ceil(((utc.getTime() - yearStart) / 86400000 + 1) / 7)
  return `${isoYear}-W${String(week).padStart(2, '0')}`
}

/**
 * Aggregate served daily history points into weekly (ISO `YYYY-Www`) or
 * monthly (`YYYY-MM`) buckets by selecting the last served session in each
 * bucket. Never interpolates or fabricates missing dates.
 */
export function aggregateValuationHistory(
  points: ValuationHistoryPoint[],
  granularity: ValuationChartGranularity,
): ValuationHistoryPoint[] {
  if (granularity === 'daily' || points.length === 0) return points

  const buckets: ValuationHistoryPoint[] = []
  const indexByKey = new Map<string, number>()

  for (const point of points) {
    const key = periodBucketKey(point.date, granularity)
    const existingIdx = indexByKey.get(key)
    if (existingIdx === undefined) {
      indexByKey.set(key, buckets.length)
      buckets.push(point)
    } else {
      buckets[existingIdx] = point
    }
  }

  return buckets
}
