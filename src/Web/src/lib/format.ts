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
