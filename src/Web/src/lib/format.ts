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

/** Locale-grouped number (e.g. `174.500.000`), falling back to the raw value. */
export function formatNumber(
  locale: string,
  value: number,
  options: Intl.NumberFormatOptions = {},
): string {
  try {
    return new Intl.NumberFormat(locale, options).format(value)
  } catch {
    return String(value)
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
 * A signed percentage for a daily change (the payload's `changePct` is already a
 * percentage, e.g. `2.00` → `+2,00%` in TR). The explicit sign is what conveys
 * direction non-colour-only (UXR-MOV-001/003/004, SCR-001 §9).
 */
export function formatSignedPercent(locale: string, value: number): string {
  const digits = formatNumber(locale, value, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    signDisplay: 'always',
  })
  return `${digits}%`
}

/** A stored ratio (e.g. `0.011225`) rendered as a percentage (`1,12%`). */
export function formatRatioPercent(locale: string, value: number): string {
  try {
    return new Intl.NumberFormat(locale, {
      style: 'percent',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value)
  } catch {
    return `${value}`
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
