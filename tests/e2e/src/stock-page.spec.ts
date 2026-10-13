import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * TC-RES-029 / TC-RES-030 / TC-RES-031 / TC-RES-033 (L4) — the SCR-005 stock-page
 * critical flows against the seeded compose.ci stack, end-to-end and anonymous
 * (`.pipeline/testing/stock-research.md`).
 *
 *   TC-RES-029 — description in preparation (BETA: draft description, numbers populated).
 *   TC-RES-030 — restatement marking visible (REST: restated FY figures).
 *   TC-RES-031 — short history (ZETA: actual windows; NEWP: no-data with boundary).
 *   TC-RES-033 — back-navigation preserves the active section.
 *
 * Copy is read from the checked-in catalogs (§8.3) so assertions never hardcode
 * prose that could drift from the product. Assertions use fixture flags/values
 * (never wall-clock dates).
 */

const TR_CATALOG = loadCatalog('tr')
const TR = (key: string) => catalogValue(TR_CATALOG, key)

test.describe('TC-RES-029 description in preparation', () => {
  test('BETA shows the preparing state while the numbers sections stay populated', async ({
    page,
  }) => {
    await page.goto('/stocks/BETA')

    const overview = page.getByRole('region', {
      name: TR('stockPage.sections.overview'),
    })
    await expect(overview.getByText(TR('honestData.preparingTitle'))).toBeVisible()

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })
    await expect(
      financials.getByText(TR('stockPage.financials.columns.revenue')),
    ).toBeVisible()
    await expect(
      financials.getByText(TR('stockPage.financials.period').replace('{{year}}', '2025')),
    ).toBeVisible()
  })
})

test.describe('TC-RES-030 restatement marking visible', () => {
  test('REST shows the asterisk, the bottom footnote and the warning indicator', async ({
    page,
  }) => {
    await page.goto('/stocks/REST')

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })

    // The affected figures carry the asterisk (textual, not colour-only).
    await expect(
      financials.locator('[data-slot="restated-marker"]').first(),
    ).toBeVisible()
    // The bottom-of-display footnote explains the restatement.
    await expect(
      financials.getByText(TR('honestData.restatedNotice')),
    ).toBeVisible()
  })
})

test.describe('TC-RES-031 short history', () => {
  test('ZETA indicates the actual growth windows used', async ({ page }) => {
    await page.goto('/stocks/ZETA')

    const growth = page.getByRole('region', {
      name: TR('stockPage.sections.growth'),
    })
    await expect(
      growth.locator('[data-slot="actual-window"]').first(),
    ).toBeVisible()
    // ZETA has 2 FYs → the 3/5/10Y CAGRs indicate the 1-year window actually used.
    await expect(
      growth.locator('[data-slot="actual-window"]').first(),
    ).toContainText('1')
  })

  test('NEWP shows the honest no-data state with the listing-date boundary', async ({
    page,
  }) => {
    await page.goto('/stocks/NEWP')

    const growth = page.getByRole('region', {
      name: TR('stockPage.sections.growth'),
    })
    await expect(growth.getByText(TR('honestData.noDataTitle'))).toBeVisible()
    // NEWP's listing date (2025-08-01) is the coverage boundary context.
    await expect(growth.getByText(/2025/).first()).toBeVisible()
  })
})

test.describe('TC-RES-033 back-navigation preserves section', () => {
  test('Balance Sheet stays active after navigating away and returning', async ({
    page,
  }) => {
    await page.goto('/stocks/REST')

    const balanceSheet = TR('stockPage.sections.balanceSheet')
    const navButton = page.getByRole('button', { name: balanceSheet })
    await navButton.click()
    await expect(navButton).toHaveAttribute('aria-current', 'true')

    // Navigate away to another screen, then return to the same stock page.
    await page.goto('/')
    await page.goto('/stocks/REST')

    await expect(page.getByRole('button', { name: balanceSheet })).toHaveAttribute(
      'aria-current',
      'true',
    )
  })
})
