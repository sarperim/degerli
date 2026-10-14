import type { Page } from '@playwright/test'

/**
 * Calendar-date expectations for date-only UI copy (TKT-res-011).
 *
 * The app renders dates through `formatAsOfDate` (`src/Web/src/lib/format.ts`) in
 * the browser, whose locale (`tr-TR`) and timezone (`Europe/Istanbul`, UTC+3) are
 * pinned by `playwright.config.ts`. Deriving the expectation the same way — by
 * evaluating `Intl.DateTimeFormat` inside the browser context — keeps the
 * assertion invariant to the Node process's own timezone, which is *not* the
 * timezone the app renders in (the Node runner is UTC on CI). Formatting an API
 * timestamp Node-side would put the two environments on different calendar days
 * between 21:00:00–23:59:59 UTC.
 */

/**
 * Mirror of `formatAsOfDate(locale='tr', isoDate)`: format an ISO date or
 * timestamp with medium date style in the browser's pinned timezone, exactly as
 * the app does. Falls back to the input for an unparseable value.
 */
export async function formatAsOfDateInBrowser(
  page: Page,
  isoDate: string,
): Promise<string> {
  return page.evaluate((value) => {
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) return value
    return new Intl.DateTimeFormat('tr', { dateStyle: 'medium' }).format(date)
  }, isoDate)
}

/**
 * Mirror of the admin screens' `datePart(iso)` (`src/Web/src/screens/admin/copy.ts`):
 * the canonical UTC calendar date (`YYYY-MM-DD`) of an ops timestamp, which is
 * what the admin summary then hands to `formatAsOfDate`. A pure slice, so
 * timezone-invariant by construction.
 */
export function datePart(iso: string): string {
  return iso.slice(0, 10)
}
