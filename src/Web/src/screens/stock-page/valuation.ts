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

/** Supported time-range presets for the historical price chart (SLICE-034). */
export type ValuationChartRange = '1W' | '1M' | '1Y' | 'max'

/** Display order for the historical price chart time-range selector. */
export const VALUATION_CHART_RANGES: readonly ValuationChartRange[] = [
  '1W',
  '1M',
  '1Y',
  'max',
]

const ISO_DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/

function parseIsoDateUtc(date: string): Date | null {
  const match = ISO_DATE_RE.exec(date)
  if (!match) return null

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])

  const utc = new Date(Date.UTC(year, month - 1, day))
  if (
    utc.getUTCFullYear() !== year ||
    utc.getUTCMonth() !== month - 1 ||
    utc.getUTCDate() !== day
  ) {
    return null
  }

  return utc
}

function subtractCalendarMonthsUtc(anchor: Date, months: number): number {
  const totalMonths = anchor.getUTCFullYear() * 12 + anchor.getUTCMonth() - months
  const targetYear = Math.floor(totalMonths / 12)
  const targetMonth = totalMonths - targetYear * 12
  const daysInTargetMonth = new Date(Date.UTC(targetYear, targetMonth + 1, 0)).getUTCDate()
  const clampedDay = Math.min(anchor.getUTCDate(), daysInTargetMonth)
  return Date.UTC(targetYear, targetMonth, clampedDay)
}

/**
 * Filter served price history points to an inclusive trailing calendar window
 * anchored to the latest valid session date present in `points`.
 * Never uses wall-clock `Date.now()`, never fabricates or interpolates points.
 */
export function filterValuationHistoryByRange(
  points: ValuationHistoryPoint[],
  range: ValuationChartRange,
): ValuationHistoryPoint[] {
  if (range === 'max' || points.length === 0) return points

  let latestAnchor: Date | null = null
  for (const point of points) {
    const parsed = parseIsoDateUtc(point.date)
    if (parsed !== null && (latestAnchor === null || parsed.getTime() > latestAnchor.getTime())) {
      latestAnchor = parsed
    }
  }

  if (latestAnchor === null) return points

  let cutoffMs: number
  if (range === '1W') {
    cutoffMs = Date.UTC(
      latestAnchor.getUTCFullYear(),
      latestAnchor.getUTCMonth(),
      latestAnchor.getUTCDate() - 7,
    )
  } else if (range === '1M') {
    cutoffMs = subtractCalendarMonthsUtc(latestAnchor, 1)
  } else {
    cutoffMs = subtractCalendarMonthsUtc(latestAnchor, 12)
  }

  return points.filter((point) => {
    const d = parseIsoDateUtc(point.date)
    return d !== null && d.getTime() >= cutoffMs
  })
}
