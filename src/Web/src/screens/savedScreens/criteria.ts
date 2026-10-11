import type { CatalogMetric, ScreenerCriterion } from '@/lib/api/contracts'
import type { CriterionDraft } from '@/screens/screener/criteria'

/**
 * Convert a saved screen's canonical criteria (`03` §4 — `%` metrics as
 * fractions) into the SCR-003 builder draft shape (display units, strings) so a
 * re-run can seed the screener context (UC-SCR-003; UXR-SCR-014).
 *
 * A metric no longer offered (hidden, UXR-SCR-011) is absent from the catalog;
 * it is carried through with its raw value rather than dropped from the display
 * — the server reports it via `droppedCriteria` (UXR-SCR-015).
 */
export function screenCriteriaToDrafts(
  criteria: ScreenerCriterion[],
  metricsByCode: Map<string, CatalogMetric>,
): CriterionDraft[] {
  return criteria.map((criterion, index) => {
    const metric = metricsByCode.get(criterion.metricCode)
    const percent = metric?.unit === '%'
    return {
      id: `criterion-${criterion.metricCode}-${index}`,
      metricCode: criterion.metricCode,
      bound: criterion.bound,
      minValue: toDisplay(criterion.minValue, percent),
      maxValue: toDisplay(criterion.maxValue, percent),
      window: criterion.window ?? 5,
    }
  })
}

/** Canonical bound value → builder display string (percentage points for `%`). */
function toDisplay(value: number | undefined, percent: boolean): string {
  if (value === undefined) return ''
  const scaled = percent ? value * 100 : value
  return String(Math.round(scaled * 1e6) / 1e6)
}
