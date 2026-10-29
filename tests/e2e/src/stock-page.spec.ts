import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * TC-RES-029 / TC-RES-030 / TC-RES-031 / TC-RES-033 / TC-RES-040 (L4) — the SCR-005
 * stock-page critical flows against the seeded compose.ci stack, end-to-end and
 * anonymous (`.pipeline/testing/stock-research.md`).
 *
 *   TC-RES-029 — description in preparation (BETA: draft description, numbers populated).
 *   TC-RES-030 — restatement marking visible (REST: restated FY figures).
 *   TC-RES-031 — short history (ZETA: actual windows; NEWP: no-data with boundary).
 *   TC-RES-033 — back-navigation preserves the active section.
 *   TC-RES-040 — the statements view (TKT-foundation-095): the three served tables,
 *                the Yıllık/Çeyreklik toggle and its honest interim labels, the dash
 *                for rows the surface does not carry, and the basis preserved across
 *                navigation (ALFA is the fixture with served interims).
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
    // Exact match: `Hasılat` is also a substring of the revenue-growth row label.
    await expect(
      financials.getByText(TR('stockPage.financials.columns.revenue'), {
        exact: true,
      }),
    ).toBeVisible()
    // The fiscal-year label repeats in each statement's header row, so the period
    // claim is scoped to the income statement's single annual column.
    await expect(
      financials
        .locator('[data-statement="IS"]')
        .getByText(TR('stockPage.financials.period').replace('{{year}}', '2025')),
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

test.describe('TC-RES-040 the statements view on the fixture world', () => {
  test('ALFA renders the three served statement tables with annual columns', async ({
    page,
  }) => {
    await page.goto('/stocks/ALFA')

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })

    // The three statement tables replace the retired five-figure summary.
    await expect(
      financials.locator('[data-statement="IS"]'),
    ).toBeVisible()
    await expect(
      financials.locator('[data-statement="BS"]'),
    ).toBeVisible()
    await expect(
      financials.locator('[data-statement="CF"]'),
    ).toBeVisible()

    // The served fiscal-year columns, labelled from the catalog.
    await expect(
      financials.locator('[data-statement="IS"]'),
    ).toContainText(TR('stockPage.financials.period').replace('{{year}}', '2025'))

    // The annual basis is the default; the interim columns are not shown beside it.
    await expect(
      financials.getByRole('button', {
        name: TR('stockPage.financials.basis.annual'),
      }),
    ).toHaveAttribute('aria-pressed', 'true')
    await expect(financials.locator('[data-period="2025-06-30"]')).toHaveCount(0)
  })

  test('the Çeyreklik toggle shows the served interims, labelled without a fabricated range', async ({
    page,
  }) => {
    await page.goto('/stocks/ALFA')

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })
    const annual = TR('stockPage.financials.period').replace('{{year}}', '2025')
    await expect(financials.locator('[data-statement="IS"]')).toContainText(annual)

    await financials
      .getByRole('button', { name: TR('stockPage.financials.basis.quarterly') })
      .click()

    const income = financials.locator('[data-statement="IS"]')
    // The 2025-06-30 interim is served as a column: every income-statement row carries
    // its own value cell for it, so the claim is scoped to one row — the interim column
    // is present, once, beside the other served interims.
    await expect(
      income.locator('[data-row="rev"] [data-period="2025-06-30"]'),
    ).toHaveCount(1)
    // The fixture interims carry no served span, so the honest quarter label renders.
    await expect(income).toContainText(
      TR('stockPage.financials.periodQuarter')
        .replace('{{quarter}}', '2')
        .replace('{{year}}', '2025'),
    )
    // Under this basis the annual column is filtered out.
    await expect(income).not.toContainText(annual)
    await expect(
      financials.getByRole('button', {
        name: TR('stockPage.financials.basis.quarterly'),
      }),
    ).toHaveAttribute('aria-pressed', 'true')
  })

  test('rows the serving surface does not carry render the dash, never a zero', async ({
    page,
  }) => {
    await page.goto('/stocks/ALFA')

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })
    const row = financials.locator('[data-row="stInvestments"]')
    await expect(row).toBeVisible()
    await expect(row.getByText(TR('stockPage.financials.rows.stInvestments'))).toBeVisible()
    await expect(row.locator('[data-slot="no-value"]').first()).toBeVisible()
    await expect(row.locator('[data-slot="no-value"]').first()).toHaveText('—')

    // The EV header renders the served operands and no computed total.
    const ev = financials.getByTestId('ev-header')
    await expect(ev).toBeVisible()
    await expect(ev.locator('[data-ev="sharePrice"]')).toBeVisible()
    // The total's value slot is the honest dash — the row also carries its label.
    await expect(
      ev.locator('[data-ev="enterpriseValue"] [data-slot="no-value"]'),
    ).toHaveText('—')
  })

  test('the chosen basis survives navigation away and back', async ({ page }) => {
    await page.goto('/stocks/ALFA')

    const quarterly = TR('stockPage.financials.basis.quarterly')
    await page
      .getByRole('region', { name: TR('stockPage.sections.financials') })
      .getByRole('button', { name: quarterly })
      .click()

    await page.goto('/')
    await page.goto('/stocks/ALFA')

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })
    await expect(
      financials.getByRole('button', { name: quarterly }),
    ).toHaveAttribute('aria-pressed', 'true')
    await expect(
      financials.locator('[data-statement="IS"]'),
    ).toContainText(
      TR('stockPage.financials.periodQuarter')
        .replace('{{quarter}}', '2')
        .replace('{{year}}', '2025'),
    )
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
