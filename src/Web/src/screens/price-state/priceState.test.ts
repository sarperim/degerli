import { describe, expect, it } from 'vitest'

import { formatSessionDate, formatUpdateTime } from '@/screens/price-state/priceState'

/**
 * TKT-foundation-049 — the price-state display helpers.
 *
 * `formatSessionDate` exists so a **date-only** `sessionDate` (`2026-10-22`) is
 * never shifted a day by the reader's zone (review round 1, DR-2: `new Date` of a
 * date-only string is UTC midnight, so formatting it in `America/New_York` says
 * "21 Eki 2026"). The expectations below are pinned literally — a civil date must
 * come back as the same civil date whatever the runner's `TZ` is.
 */
describe('TKT-foundation-049 — price-state date/time helpers', () => {
  it('keeps a date-only sessionDate on its own civil date (no west-of-UTC shift)', () => {
    expect(formatSessionDate('tr', '2026-10-22')).toBe('22 Eki 2026')
    expect(formatSessionDate('en', '2026-10-22')).toBe('Oct 22, 2026')
    // Month boundary: no day-1 → previous-month drift.
    expect(formatSessionDate('tr', '2026-11-01')).toBe('1 Kas 2026')
  })

  it('formats the intraday asOf in the Istanbul session clock, not the browser zone', () => {
    expect(formatUpdateTime('tr', '2026-10-22T12:15:00Z')).toBe('15:15')
    expect(formatUpdateTime('tr', '2026-10-22T15:05:00Z')).toBe('18:05')
  })

  it('falls back to the raw value rather than inventing a date or time', () => {
    expect(formatSessionDate('tr', 'not-a-date')).toBe('not-a-date')
    expect(formatUpdateTime('tr', 'not-a-date')).toBe('not-a-date')
  })
})
