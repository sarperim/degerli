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

/**
 * Locale-grouped number (e.g. `174.500.000`), falling back to the raw value.
 *
 * Defaults to at most two fraction digits (canonical ratio metrics such as a
 * P/E of 12.4); callers that need other precision pass `options`.
 */
export function formatNumber(
  locale: string,
  value: number,
  options: Intl.NumberFormatOptions = { maximumFractionDigits: 2 },
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
 * A signed daily-change percentage from the API's **fraction** unit: `0.014735`
 * → `+1,47%` in TR (`-0.1` → `-10,00%`, `0.02` → `+2,00%`).
 *
 * The API serves every ratio/percentage metric as a decimal fraction
 * (`03` §2/§4: `0.02` = +2%; a screener `roe` of `0.20` = 20%), so the ×100
 * happens here — this formatter is the unit boundary, and no caller may pass an
 * already-scaled percentage (TKT-foundation-036: the pre-036 code skipped the
 * scaling and rendered every home-screen change 100× too small).
 *
 * The explicit sign is what conveys direction non-colour-only
 * (UXR-MOV-001/003/004, SCR-001 §9).
 */
export function formatSignedPercent(locale: string, fraction: number): string {
  const digits = formatNumber(locale, fraction * 100, {
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
 * Format a canonical `%` metric stored as a fraction (`0.15` → `15%`). The API
 * serves `%`-unit metrics as fractions (`03` §4); the UI adds the percent sign.
 *
 * `maximumFractionDigits` defaults to 1 (the DCF sensitivity axes and verdict);
 * the stock page passes 2 for its growth/profitability figures.
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

/** Format a monetary amount in its own currency code (e.g. TRY) for dividends. */
export function formatCurrency(
  locale: string,
  value: number,
  currency: string,
): string {
  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      maximumFractionDigits: 4,
    }).format(value)
  } catch {
    return `${value} ${currency}`
  }
}
