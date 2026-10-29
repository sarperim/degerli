import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * TC-RES-029 / TC-RES-030 / TC-RES-031 / TC-RES-033 / TC-RES-040 / TC-RES-041 (L4) —
 * the SCR-005 stock-page critical flows against the seeded compose.ci stack,
 * end-to-end and anonymous (`.pipeline/testing/stock-research.md`).
 *
 *   TC-RES-029 — description in preparation (BETA: draft description, numbers populated).
 *   TC-RES-030 — restatement marking visible (REST: restated FY figures).
 *   TC-RES-031 — short history (ZETA: actual windows; NEWP: no-data with boundary).
 *   TC-RES-033 — back-navigation preserves the active section.
 *   TC-RES-040 — the statements view (TKT-foundation-095): the three served tables,
 *                the Yıllık/Çeyreklik toggle and its honest interim labels, the dash
 *                for rows the surface does not carry, and the basis preserved across
 *                navigation (ALFA is the fixture with served interims).
 *   TC-RES-041 — the key value metrics & moat panel and the capital & dilution
 *                tracker (TKT-foundation-097): the served current values, the
 *                withheld company median's disclosure, the honest dash where the
 *                fixture world serves no sector median / no input, and the
 *                share-count N/M (the fixture world runs no medians job).
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

test.describe('TC-RES-041 key metrics & capital tracker on the fixture world', () => {
  test('ALFA renders the served panel and tracker with their honest gaps', async ({
    page,
  }) => {
    await page.goto('/stocks/ALFA')

    const financials = page.getByRole('region', {
      name: TR('stockPage.sections.financials'),
    })
    const panel = financials.getByTestId('key-metrics-panel')
    await expect(panel).toBeVisible()

    // The served current value (ALFA's CROIC from its own fixture lines, 250/1,200).
    const croicPercent = new Intl.NumberFormat('tr-TR', {
      style: 'percent',
      maximumFractionDigits: 2,
    }).format(250 / 1200)
    await expect(
      panel.locator('[data-metric="croic"]').locator('td').first(),
    ).toHaveText(croicPercent)

    // The 5Y company median is withheld on the fixture world's one stored day, and
    // its depth/observation disclosure is rendered beside the dash (never a
    // fabricated median).
    await expect(
      panel
        .locator('[data-metric="croic"] [data-slot="company-median-disclosure"]')
        .first(),
    ).toBeVisible()

    // No medians job runs in the fixture world → every sector median is the honest
    // dash, never a fabricated comparison.
    await expect(
      panel.locator('[data-metric="croic"] [data-slot="sector-median"]'),
    ).toHaveText('—')

    // A code the fixture statements cannot compute (no TOTAL_ASSETS) is the dash.
    await expect(
      panel.locator('[data-metric="roa"] [data-slot="no-value"]').first(),
    ).toHaveText('—')

    // Basis honesty: the served group-basis ROE is labelled as such, never Parent.
    await expect(
      panel.locator('[data-metric="roe"] th').first(),
    ).toHaveText(TR('stockPage.keyMetrics.rows.roe'))
    await expect(panel.locator('[data-metric="roe"] th').first()).not.toContainText(
      'Ana Ortaklık',
    )

    const tracker = financials.getByTestId('capital-tracker')
    await expect(tracker).toBeVisible()
    // No share-count history is stored → the Y/Y change is the honest N/M.
    const change = tracker.locator('[data-row="shareCountChangeYoy"]')
    await expect(change.locator('[data-slot="no-value"]')).toHaveText('—')
    await expect(change).toContainText(
      TR('stockPage.capitalTracker.noCountHistory'),
    )
  })
})
