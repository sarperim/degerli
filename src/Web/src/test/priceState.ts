/**
 * Shared expectations for the price-state suites (TKT-foundation-049).
 *
 * Both the SCR-001 (`MarketOverviewPage.test.tsx`) and SCR-005
 * (`StockPriceState.test.tsx`) cases need the same two helpers — an i18n
 * `{{var}}` template fill and the BIST session clock the doubles are written in.
 * Kept here rather than duplicated per suite (review round 1, DR-8).
 */

/** Fill an i18n `{{var}}` template the way i18next would. */
export function fillTemplate(
  template: string,
  vars: Record<string, string | number>,
): string {
  return Object.entries(vars).reduce(
    (text, [name, value]) => text.replaceAll(`{{${name}}}`, String(value)),
    template,
  )
}

/**
 * Format an instant in the exchange's session clock (Istanbul, UTC+03) — the
 * clock the price-state doubles and the header both speak, independent of the
 * machine running the test.
 */
export function sessionClockTime(iso: string): string {
  return new Intl.DateTimeFormat('tr', {
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone: 'Europe/Istanbul',
  }).format(new Date(iso))
}
