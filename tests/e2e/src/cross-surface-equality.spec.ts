import { expect, test } from '@playwright/test'

import { API_BASE_URL } from './config'
import { tagWithClientIp } from './helpers/accounts'
import { catalogValue, loadCatalog } from './helpers/catalog'
import {
  buildCanonicalScreen,
  parseRenderedNumber,
  runScreener,
  type ScreenerRunResponse,
} from './helpers/screener'

/**
 * TC-XC-008 (L4) — cross-surface metric equality.
 *
 * The same stock/metric/date must be identical wherever it appears: ALFA's P/E
 * 10.0 in the screener results and the stock page valuation section (both served
 * from the same canonical source, BR-MDF-009); the price the DCF calculator uses
 * (20.00) equals the canonical `daily_prices` T row; and the dashboard's market
 * P/E equals the stored snapshot aggregate the API serves.
 *
 * The sweep asserts equality at two layers in one session: the canonical values
 * in the served payloads (byte-identical at the data boundary, NFR-SCR-003/
 * RES-003/VAL-003) and the numbers the three surfaces render from them.
 *
 * Source: `.pipeline/testing/cross-cutting.md` TC-XC-008 (UXR-G-011, BR-MDF-009;
 * NFR-SCR-003/NFR-RES-003/NFR-VAL-003).
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

const ALFA_PE = 10 // TC-SCR-002 golden
const ALFA_PRICE = 20 // TC-VAL-012 golden

interface ValuationResponse {
  asOf: string
  current: Record<string, number | null>
}

interface DcfResponse {
  price: { value: number; asOf: string; stale: boolean }
}

interface MarketOverviewResponse {
  asOf: string
  marketPe: number | null
}

test.describe('TC-XC-008 cross-surface metric equality', () => {
  test('one number every surface: P/E, price and the market aggregate agree', async ({
    page,
    request,
  }) => {
    const trLocale = 'tr-TR'

    // Every browser request carries its own client IP so the global/auth rate
    // limits never throttle an unrelated flow during this spec (I-XC-2).
    tagWithClientIp(page)

    // ---- Dashboard market P/E === the stored snapshot aggregate. ------------
    const marketResponse = await request.get(
      `${API_BASE_URL}/api/v1/market/overview?block=valuation`,
    )
    expect(marketResponse.status()).toBe(200)
    const market = (await marketResponse.json()) as MarketOverviewResponse

    await page.goto('/')
    const expectedMarketPe = new Intl.NumberFormat(trLocale, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(market.marketPe as number)
    await expect(page.getByTestId('market-pe')).toHaveText(expectedMarketPe)

    // ---- Screener: ALFA's P/E comes from the canonical run payload. ---------
    await page.goto('/screener')
    await buildCanonicalScreen(page)
    const run = await runScreener(page)

    const alfaScreenerRow = run.rows.find((row) => row.symbol === 'ALFA')
    expect(alfaScreenerRow).toBeDefined()
    const peFromScreenerApi = alfaScreenerRow!.values.pe
    expect(peFromScreenerApi).toBeCloseTo(ALFA_PE, 4)

    const peScreenerRendered = parseRenderedNumber(
      (await page.getByTestId('result-value-ALFA-pe').textContent()) ?? '',
    )
    expect(peScreenerRendered).toBeCloseTo(ALFA_PE, 4)

    // ---- Stock page valuation section: the same P/E, same date. -------------
    const [valuationResponse] = await Promise.all([
      page.waitForResponse((candidate) =>
        candidate.url().includes('/api/v1/stocks/ALFA/valuation'),
      ),
      page.goto('/stocks/ALFA'),
    ])
    expect(valuationResponse.status()).toBe(200)
    const valuation = (await valuationResponse.json()) as ValuationResponse
    const peFromValuationApi = valuation.current.pe
    expect(peFromValuationApi).toBeCloseTo(ALFA_PE, 4)

    const valuationRegion = page.getByRole('region', {
      name: TR('stockPage.sections.valuation'),
    })
    const peValuationRendered = parseRenderedNumber(
      (await valuationRegion
        .locator('[data-metric="pe"] [data-slot="stock-value"]')
        .first()
        .textContent()) ?? '',
    )
    expect(peValuationRendered).toBeCloseTo(ALFA_PE, 4)

    // ---- DCF calculator: the price each surface derives from. ---------------
    const [dcfResponse] = await Promise.all([
      page.waitForResponse(
        (candidate) =>
          /\/api\/v1\/stocks\/ALFA\/dcf$/.test(candidate.url()) &&
          candidate.request().method() === 'GET',
      ),
      page.goto('/stocks/ALFA/dcf'),
    ])
    expect(dcfResponse.status()).toBe(200)
    const dcf = (await dcfResponse.json()) as DcfResponse
    expect(dcf.price.value).toBeCloseTo(ALFA_PRICE, 4)

    const priceRendered = parseRenderedNumber(
      (await page.getByTestId('dcf-price').textContent()) ?? '',
    )
    expect(priceRendered).toBeCloseTo(ALFA_PRICE, 4)

    // ---- One number, every surface: data boundary + rendered equality. ------
    // Byte-identical at the data boundary (the canonical source, BR-MDF-009).
    expect(peFromScreenerApi).toBe(peFromValuationApi)
    // The three surfaces render the same number.
    expect(peScreenerRendered).toBe(peValuationRendered)
    expect(peScreenerRendered).toBe(peFromScreenerApi)
    expect(priceRendered).toBe(dcf.price.value)

    // Same stock/metric/date: every surface carries the same data-as-of date.
    expect(valuation.asOf).toBe(run.asOf)
    expect(dcf.price.asOf).toBe(run.asOf)
    expect(market.asOf).toBe(run.asOf)
  })
})
