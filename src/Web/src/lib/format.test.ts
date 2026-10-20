import { describe, expect, it } from 'vitest'

import {
  formatPercent,
  formatRatioPercent,
  formatSignedPercent,
} from '@/lib/format'

/**
 * TC-MOV-031 (`.pipeline/testing/market-overview.md` §9) — the API's
 * **fraction** unit contract at the formatter boundary (`03` §2/§4: every
 * ratio/percentage metric travels as a decimal fraction, `0.02` = +2%).
 *
 * TKT-foundation-036: `formatSignedPercent` used to render its input verbatim,
 * so a served `changePct: 0.014735` (+1,47%) displayed as "+0,01%" — the whole
 * home screen was 100× too small. These are the unit-level pins that make that
 * class impossible: the signed-change formatter scales, while the
 * already-fraction-aware `formatRatioPercent`/`formatPercent` keep their
 * (unchanged) behavior and must never be double-scaled.
 */

describe('TC-MOV-031 formatSignedPercent renders an API fraction as a percentage', () => {
  it('renders the live-served fractional change 0.014735 as +1,47% (TR)', () => {
    expect(formatSignedPercent('tr', 0.014735)).toBe('+1,47%')
  })

  it('renders 0.02 as +2,00% and −0.1 as −10,00% (TR)', () => {
    expect(formatSignedPercent('tr', 0.02)).toBe('+2,00%')
    expect(formatSignedPercent('tr', -0.1)).toBe('-10,00%')
  })

  it('renders a true decline (XU100 −0.024875) as −2,49% (TR)', () => {
    expect(formatSignedPercent('tr', -0.024875)).toBe('-2,49%')
  })

  it('always shows an explicit sign, including for a flat change', () => {
    expect(formatSignedPercent('tr', 0)).toBe('+0,00%')
  })

  it('scales by 100 in EN locale formatting too (+1.47%)', () => {
    expect(formatSignedPercent('en', 0.014735)).toBe('+1.47%')
    expect(formatSignedPercent('en', -0.1)).toBe('-10.00%')
  })
})

describe('TC-MOV-031 the already-fraction-aware formatters are not double-scaled', () => {
  it('formatRatioPercent renders the dividend yield 0.011225 as 1,12%', () => {
    // TR's ICU percent pattern puts the sign before the number (`%1,12`); the
    // value — the unit behavior this ticket pins — is 1,12%, never 112,25%.
    expect(formatRatioPercent('tr', 0.011225)).toBe('%1,12')
    expect(formatRatioPercent('en', 0.011225)).toBe('1.12%')
    expect(formatRatioPercent('en', 0.011225)).not.toBe('112.25%')
  })

  it('formatPercent renders a stock/DCF % metric 0.182 as 18,2%', () => {
    expect(formatPercent('en', 0.182)).toBe('18.2%')
    expect(formatPercent('tr', 0.182)).toBe('%18,2')
    // `maximumFractionDigits` only (no minimum) — 0.182 with 2 digits is 18.2%.
    expect(formatPercent('en', 0.182, 2)).toBe('18.2%')
  })
})
