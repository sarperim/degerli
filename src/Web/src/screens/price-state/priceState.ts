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
