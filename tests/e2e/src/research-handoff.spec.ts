import { expect, test } from '@playwright/test'

import { tagWithClientIp } from './helpers/accounts'
import { catalogValue, loadCatalog } from './helpers/catalog'
import {
  buildCanonicalScreen,
  parseRenderedNumber,
  runScreener,
} from './helpers/screener'

/**
 * TC-RES-028 (L4) — research hand-off (SC-002 research stage).
 *
 * From a screener result row the stock page opens; all seven sections are
 * available and navigable; canonical figures carry their as-of dates; the DCF
 * hand-off opens the calculator for this stock.
 *
 * Source: `.pipeline/testing/stock-research.md` TC-RES-028 (UC-RES-003 main,
 * UC-RES-005, FR-RES-018, UXR-RES-023, SCR-005 critical flow 1). Copy is read
 * from the checked-in catalogs (§8.3).
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

const SEVEN_SECTIONS = [
  TR('stockPage.sections.overview'),
  TR('stockPage.sections.valuation'),
  TR('stockPage.sections.financials'),
  TR('stockPage.sections.profitability'),
  TR('stockPage.sections.growth'),
  TR('stockPage.sections.balanceSheet'),
  TR('stockPage.sections.dividends'),
]

test.describe('TC-RES-028 research hand-off', () => {
  test('screener row → stock page with seven sections → DCF hand-off', async ({
    page,
  }) => {
    // Every browser request carries its own client IP so the global/auth rate
    // limits never throttle an unrelated flow during this spec (I-XC-2).
    tagWithClientIp(page)

    // ---- From a screener result row the stock page opens. -------------------
    await page.goto('/screener')
    await buildCanonicalScreen(page)
    await runScreener(page)

    await page.getByTestId('result-row-ALFA').getByRole('link').click()
    await expect(page).toHaveURL(/\/stocks\/ALFA$/)
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: TR('screens.stockPage.title'),
      }),
    ).toBeVisible()

    // ---- All seven sections available and navigable. ------------------------
    expect(SEVEN_SECTIONS).toHaveLength(7)
    for (const label of SEVEN_SECTIONS) {
      const navButton = page.getByRole('button', { name: label })
      await expect(navButton).toBeVisible()
      await navButton.click()
      await expect(navButton).toHaveAttribute('aria-current', 'true')
      await expect(page.getByRole('region', { name: label })).toBeVisible()
    }

    // ---- Canonical figures with their as-of dates. --------------------------
    const valuation = page.getByRole('region', {
      name: TR('stockPage.sections.valuation'),
    })
    // ALFA's canonical P/E (fixture golden: 10.0) is served on the stock page.
    const peValue = valuation
      .locator('[data-metric="pe"] [data-slot="stock-value"]')
      .first()
    await expect(peValue).toBeVisible()
    expect(parseRenderedNumber((await peValue.textContent()) ?? '')).toBeCloseTo(
      10,
      4,
    )
    await expect(
      valuation.locator('[data-slot="as-of-date"]').first(),
    ).toBeVisible()

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })
    await expect(
      financials.getByText(TR('stockPage.financials.columns.revenue')),
    ).toBeVisible()
    await expect(
      financials.locator('[data-slot="as-of-date"]').first(),
    ).toBeVisible()

    // ---- The DCF hand-off opens the calculator for this stock. --------------
    await page.getByRole('link', { name: TR('stockPage.dcfHandoff') }).click()
    await expect(page).toHaveURL(/\/stocks\/ALFA\/dcf$/)
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: TR('screens.dcfCalculator.title'),
      }),
    ).toBeVisible()
    await expect(page.getByTestId('dcf-baseline-version')).toBeVisible()
    // The hand-off is for this stock: the served baseline is ALFA's.
    await expect(page.getByTestId('dcf-price')).toBeVisible()
  })
})
