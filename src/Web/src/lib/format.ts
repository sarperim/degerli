/**
 * Locale-aware formatting helpers (TKT-foundation-011).
 *
 * Kept out of component modules so component files export only React components
 * (react-refresh boundary).
 */

/** Format an ISO date for display in the active language; falls back to input. */
export function formatAsOfDate(locale: string, isoDate: string): string {
  const date = new Date(isoDate)
  if (Number.isNaN(date.getTime())) return isoDate
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(date)
  } catch {
    return isoDate
  }
}

/** Format a decimal for display in the active language (grouping + locale separators). */
export function formatDecimal(
  locale: string,
  value: number,
  maximumFractionDigits = 2,
  minimumFractionDigits = 0,
): string {
  try {
    return new Intl.NumberFormat(locale, {
      maximumFractionDigits,
      minimumFractionDigits,
    }).format(value)
  } catch {
    return String(value)
  }
}

/**
 * Format a canonical rate (a fraction, 0.12) as a localized percentage
 * (`12%` / `%12`). Used for the DCF sensitivity axes and the verdict.
 */
export function formatPercent(
  locale: string,
  fraction: number,
  maximumFractionDigits = 1,
): string {
  try {
    return new Intl.NumberFormat(locale, {
      style: 'percent',
      maximumFractionDigits,
    }).format(fraction)
  } catch {
    return `${(fraction * 100).toFixed(maximumFractionDigits)}%`
  }
}
