import { expect, test } from '@playwright/test'

import { tagWithClientIp } from './helpers/accounts'
import { catalogValue, loadCatalog } from './helpers/catalog'
import { buildCanonicalScreen, runScreener } from './helpers/screener'

/**
 * TC-XC-001 (L4) — the SC-002 core loop, anonymous, end-to-end.
 *
 * One anonymous browser session walks the complete discover → screen → research →
 * value → decide chain without an account:
 *
 *   dashboard (equity blocks + macro) → Screener → build "P/E at most 15" +
 *   "ROE at least 15%" → run → 3 results (ALFA/REST/UNSEC) with values and as-of →
 *   open ALFA's stock page → move through the sections (canonical figures, honest
 *   marks) → open the DCF calculator → baseline result visible → change the
 *   discount rate → recompute → read the plain-language verdict.
 *
 * Source: `.pipeline/testing/cross-cutting.md` TC-XC-001 (SC-002, OBJ-002;
 * architecture `01` §6.1 e2e job, §8.1 M-5). Copy is read from the checked-in
 * catalogs (§8.3); values are asserted from the served payloads, never hardcoded
 * wall-clock dates.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

test.describe('TC-XC-001 SC-002 core loop, anonymous, end-to-end', () => {
  test('dashboard → screener → stock page → DCF verdict without an account', async ({
    page,
  }) => {
    const trLocale = 'tr-TR'

    // Every browser request carries its own client IP so the global/auth rate
    // limits never throttle an unrelated flow during this spec (I-XC-2).
    tagWithClientIp(page)

    // ---- 1. Dashboard renders (equity blocks + macro), anonymously. ----------
    await page.goto('/')

    await expect(page.getByText(TR('header.anonymous'))).toBeVisible()
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: TR('screens.marketOverview.title'),
      }),
    ).toBeVisible()
    await expect(page.getByTestId('market-block-valuation')).toBeVisible()
    await expect(page.getByTestId('market-pe')).toBeVisible()
    await expect(page.getByTestId('market-block-macro')).toBeVisible()

    // ---- 2. Open the Screener from the primary navigation. -------------------
    await page.getByRole('link', { name: TR('nav.screener') }).click()
    await expect(page).toHaveURL(/\/screener$/)
    await expect(
      page.getByRole('heading', { level: 1, name: TR('screens.screener.title') }),
    ).toBeVisible()

    // ---- 3. Build "P/E at most 15" + "ROE at least 15%" and run. -------------
    await buildCanonicalScreen(page)
    const result = await runScreener(page)

    expect(result.matchCount).toBe(3)
    expect(result.excludedCount).toBe(2)
    expect(result.rows.map((row) => row.symbol).sort()).toEqual([
      'ALFA',
      'REST',
      'UNSEC',
    ])

    // 3 results with their values and the data-as-of date rendered.
    await expect(page.getByTestId('results')).toBeVisible()
    await expect(page.getByTestId('match-count')).toContainText('3')
    await expect(page.getByTestId('excluded-count')).toContainText('2')
    await expect(page.getByTestId('results-asof')).toBeVisible()
    for (const symbol of ['ALFA', 'REST', 'UNSEC']) {
      await expect(page.getByTestId(`result-row-${symbol}`)).toBeVisible()
    }
    await expect(page.getByTestId('result-value-ALFA-pe')).toContainText(/\d/)
    await expect(page.getByTestId('result-value-ALFA-roe')).toContainText('%')

    // ---- 4. Open ALFA's stock page from the result row. ---------------------
    await page.getByTestId('result-row-ALFA').getByRole('link').click()
    await expect(page).toHaveURL(/\/stocks\/ALFA$/)

    // ---- 5. Move through the seven sections (canonical figures, honest marks).
    const sectionLabels = [
      TR('stockPage.sections.overview'),
      TR('stockPage.sections.valuation'),
      TR('stockPage.sections.financials'),
      TR('stockPage.sections.profitability'),
      TR('stockPage.sections.growth'),
      TR('stockPage.sections.balanceSheet'),
      TR('stockPage.sections.dividends'),
    ]
    for (const label of sectionLabels) {
      const navButton = page.getByRole('button', { name: label })
      await navButton.click()
      await expect(navButton).toHaveAttribute('aria-current', 'true')
      await expect(page.getByRole('region', { name: label })).toBeVisible()
    }

    // A canonical figure and its honest as-of marker are present (valuation P/E).
    const valuation = page.getByRole('region', {
      name: TR('stockPage.sections.valuation'),
    })
    await expect(
      valuation.locator('[data-slot="as-of-date"]').first(),
    ).toBeVisible()

    // ---- 6. Open the DCF calculator for this stock. -------------------------
    await page.getByRole('link', { name: TR('stockPage.dcfHandoff') }).click()
    await expect(page).toHaveURL(/\/stocks\/ALFA\/dcf$/)
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: TR('screens.dcfCalculator.title'),
      }),
    ).toBeVisible()

    // ---- 7. Baseline result visible before any change. ----------------------
    await expect(page.getByTestId('dcf-baseline-version')).toBeVisible()
    const fairValue = page.getByTestId('dcf-fair-value')
    await expect(fairValue).toBeVisible()
    await expect(fairValue).not.toHaveText('—')
    await expect(page.getByTestId('dcf-price')).toContainText(/\d/)
    await expect(page.getByTestId('dcf-verdict')).toBeVisible()

    // ---- 8. Change the discount rate → recompute → verdict updates. ---------
    const before = await fairValue.textContent()

    const [computeResponse] = await Promise.all([
      page.waitForResponse(
        (candidate) =>
          candidate.url().includes('/dcf/compute') &&
          candidate.request().method() === 'POST',
      ),
      page.getByTestId('dcf-param-discount_rate').fill('15'),
    ])
    expect(computeResponse.status()).toBe(200)
    const computed = (await computeResponse.json()) as { fairValuePerShare: number }

    const expectedFairValue = new Intl.NumberFormat(trLocale, {
      maximumFractionDigits: 4,
      minimumFractionDigits: 2,
    }).format(computed.fairValuePerShare)

    await expect(fairValue).toHaveText(expectedFairValue)
    expect(await fairValue.textContent()).not.toBe(before)

    // The plain-language verdict is framed "based on your assumptions".
    await expect(page.getByTestId('dcf-verdict')).toContainText(TR('dcf.framing'))
    await expect(page.getByTestId('dcf-verdict')).toHaveAttribute(
      'data-direction',
      /below|above|equal/,
    )
  })
})
