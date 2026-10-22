/**
 * The intraday/close price-state display helpers (TKT-foundation-049).
 *
 * Kept out of the component module so it exports only React components
 * (react-refresh boundary, the `stock-page/valuation.ts` precedent).
 */

/** The exchange's session clock: BIST trades in Istanbul local time (UTC+03). */
export const SESSION_TIME_ZONE = 'Europe/Istanbul'

/**
 * The last-update time of an intraday value (`asOf` ISO instant → `15:15`).
 *
 * The server's `asOf` is an instant; the user reads it in the session's own
 * clock, so the zone is explicit rather than the browser's — a reader abroad
 * still sees when the BIST sweep ran. Falls back to the raw value on an
 * unparseable input rather than inventing a time.
 */
export function formatUpdateTime(locale: string, asOf: string): string {
  const date = new Date(asOf)
  if (Number.isNaN(date.getTime())) return asOf
  try {
    return new Intl.DateTimeFormat(locale, {
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
      timeZone: SESSION_TIME_ZONE,
    }).format(date)
  } catch {
    return asOf
  }
}

/**
 * Format a **date-only** `sessionDate` (`2026-10-22`) without a day shift.
 *
 * `new Date('2026-10-22')` is UTC midnight, so formatting it in the browser's
 * zone renders the previous day west of UTC (`America/New_York` → "Oct 21").
 * A `sessionDate` is a civil date, not an instant, so it is parsed as UTC
 * midnight and formatted **in UTC** — every reader sees the session's own date.
 * (036's `formatAsOfDate` is untouched; this is the date-only call site.)
 */
export function formatSessionDate(locale: string, sessionDate: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(sessionDate)
  const date = parts
    ? new Date(Date.UTC(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3])))
    : new Date(sessionDate)
  if (Number.isNaN(date.getTime())) return sessionDate
  try {
    return new Intl.DateTimeFormat(locale, {
      dateStyle: 'medium',
      timeZone: 'UTC',
    }).format(date)
  } catch {
    return sessionDate
  }
}
