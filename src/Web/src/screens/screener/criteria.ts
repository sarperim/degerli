import type {
  CatalogMetric,
  CriterionBound,
  CagrWindow,
  MetricFamily,
  ScreenerCriterion,
} from '@/lib/api/contracts'
import type { TFunction } from 'i18next'
import { isPercentUnit, unitSuffix } from '@/lib/units'

/**
 * Client-side criterion model for the SCR-003 builder (UXR-SCR-001..003).
 *
 * A draft holds the *user-entered* values as strings (free typing), keyed to a
 * catalog metric. Input/display are in percentage points for `%` metrics — the
 * analysis's plain-language phrasing is "ROE min 15%", "dividend yield min 3%" —
 * and canonical fractions cross the API boundary (`03` §4: `roe minValue 0.15`).
 * Ratio metrics (`unit: "x"`) pass through unchanged.
 */

export interface CriterionDraft {
  /** Stable local id for React keys / removal. */
  id: string
  metricCode: string
  bound: CriterionBound
  /** User-entered min bound, in display units (percentage points for `%`). */
  minValue: string
  /** User-entered max bound, in display units. */
  maxValue: string
  /** CAGR window for growth metrics (FR-SCR-015); ignored otherwise. */
  window: CagrWindow
}

/** i18n keys for the five family headings, in catalog order. */
export const FAMILY_LABEL_KEYS: Record<MetricFamily, string> = {
  valuation: 'screener.families.valuation',
  quality: 'screener.families.quality',
  growth: 'screener.families.growth',
  financial_health: 'screener.families.financial_health',
  dividends: 'screener.families.dividends',
}

/** The selectable CAGR windows, in order (BR-SCR-005). */
export const CAGR_WINDOWS: CagrWindow[] = [3, 5, 10]

/** True when the metric's canonical value is a fraction displayed as a percent. */
export function isPercentMetric(metric: CatalogMetric): boolean {
  return isPercentUnit(metric.unit)
}

/** Plain-language metric label in the active language (NFR-SCR-002). */
export function metricLabel(
  metric: CatalogMetric,
  language: string,
): string {
  return language === 'en' ? metric.labelEn : metric.labelTr
}

let draftSequence = 0

/** Build a fresh draft for a metric with sensible defaults. */
export function createDraft(metric: CatalogMetric): CriterionDraft {
  draftSequence += 1
  return {
    id: `criterion-${metric.metricCode}-${draftSequence}`,
    metricCode: metric.metricCode,
    bound: 'min',
    minValue: '',
    maxValue: '',
    window: 5,
  }
}

/** Parse a user-entered bound value; accepts a comma decimal separator. */
export function parseBoundValue(input: string): number | null {
  const trimmed = input.trim()
  if (trimmed === '') return null
  const normalized = trimmed.replace(',', '.')
  const value = Number(normalized)
  return Number.isFinite(value) ? value : null
}

/**
 * Whether a draft is complete enough to run: the required bound value(s) parse
 * and, for a range, min ≤ max.
 */
export function isDraftValid(draft: CriterionDraft): boolean {
  const min = parseBoundValue(draft.minValue)
  const max = parseBoundValue(draft.maxValue)
  switch (draft.bound) {
    case 'min':
      return min !== null
    case 'max':
      return max !== null
    case 'range':
      return min !== null && max !== null && min <= max
    default:
      return false
  }
}

/**
 * Convert a draft to a wire criterion against its catalog metric. `%` metrics are
 * converted from percentage points to canonical fractions; ratio metrics pass
 * through. Growth metrics carry their selected window.
 */
export function draftToCriterion(
  draft: CriterionDraft,
  metric: CatalogMetric,
): ScreenerCriterion {
  const percent = isPercentMetric(metric)
  const scale = (displayValue: number) => (percent ? displayValue / 100 : displayValue)
  const min = parseBoundValue(draft.minValue)
  const max = parseBoundValue(draft.maxValue)

  const criterion: ScreenerCriterion = {
    metricCode: draft.metricCode,
    bound: draft.bound,
  }
  if (draft.bound === 'min' || draft.bound === 'range') {
    if (min !== null) criterion.minValue = scale(min)
  }
  if (draft.bound === 'max' || draft.bound === 'range') {
    if (max !== null) criterion.maxValue = scale(max)
  }
  if (metric.isCagr) {
    criterion.window = draft.window
  }
  return criterion
}

/**
 * Format a canonical value for display: percent for `%` metrics, raw otherwise —
 * with the unit resolved to its localized label (`src/lib/units.ts`), never the raw
 * server code (TKT-foundation-072).
 */
export function formatMetricValue(
  value: number | null | undefined,
  metric: CatalogMetric | undefined,
  locale: string,
  t: TFunction,
): string {
  if (value === null || value === undefined) return '—'
  const percent = metric ? isPercentMetric(metric) : false
  const display = percent ? value * 100 : value
  const formatted = new Intl.NumberFormat(locale, {
    maximumFractionDigits: 2,
  }).format(display)
  // A metric the catalog no longer carries makes no unit claim at all.
  if (!metric) return formatted
  return `${formatted}${unitSuffix(metric.unit, t)}`
}
