import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * TC-RES-035 / TC-RES-036 (L4) — the SCR-002 discovery flows against the seeded
 * compose.ci stack, end-to-end and anonymous (`.pipeline/testing/stock-research.md`).
 *
 * TC-RES-035: header search → stock page; viewing all matches → list with the
 * query applied; a search miss → explicit no-match state; a dashboard sector link
 * → list pre-filtered. TC-RES-036: every SCR-002 surface/flow runs without an
 * account — no save prompt or auth wall appears anywhere in the research path.
 *
 * Copy is read from the checked-in catalogs (§8.3) so assertions never hardcode
 * prose that could drift from the product.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

test.describe('TC-RES-035 header search → stock page; sector prefill; search miss', () => {
  test('selecting a header-search match opens that stock page directly', async ({
    page,
  }) => {
    await page.goto('/')

    await page.getByLabel(TR('header.searchLabel')).fill('alfa')
    await page.getByRole('option', { name: /Alfa Teknoloji/ }).click()

    await expect(page).toHaveURL(/\/stocks\/ALFA$/)
  })

  test('viewing all matches opens the list with the query applied', async ({
    page,
  }) => {
    await page.goto('/')

    await page.getByLabel(TR('header.searchLabel')).fill('alfa')
    await page.getByRole('button', { name: TR('headerSearch.viewAll') }).click()

    await expect(page).toHaveURL(/\/stocks\?q=alfa$/)
    await expect(page.getByTestId('stock-row-ALFA')).toBeVisible()
    await expect(page.getByTestId('active-search')).toContainText('alfa')
  })

  test('a query matching nothing shows an explicit no-match state', async ({
    page,
  }) => {
    await page.goto('/')

    await page.getByLabel(TR('header.searchLabel')).fill('zzzznope')

    await expect(page.getByText(TR('headerSearch.noMatches'))).toBeVisible()
  })

  test('a dashboard sector link opens the list pre-filtered to that sector', async ({
    page,
  }) => {
    await page.goto('/stocks?sector=A')

    await expect(page.getByLabel(TR('stockList.sectorLabel'))).toHaveValue('A')
    await expect(page.getByTestId('stock-row-ALFA')).toBeVisible()
    await expect(page.getByTestId('stock-row-ZETA')).toHaveCount(0)
  })
})

test.describe('TC-RES-036 public access without an account', () => {
  test('the stock list and its flows run anonymously with no auth wall', async ({
    page,
  }) => {
    await page.goto('/stocks')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      TR('screens.stockList.title'),
    )
    // Anonymous header state — no sign-in prompt, no auth-wall dialog.
    await expect(page.getByText(TR('header.anonymous'))).toBeVisible()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByTestId('stock-row-ALFA')).toBeVisible()

    // Filter + search operate without an account.
    await page.getByLabel(TR('stockList.searchLabel')).fill('beta')
    await expect(page.getByTestId('stock-row-BETA')).toBeVisible()

    // Row activation reaches a stock page anonymously (still no auth wall).
    await page.getByRole('link', { name: /Beta Yazılım/ }).click()
    await expect(page).toHaveURL(/\/stocks\/BETA$/)
    await expect(page.getByRole('dialog')).toHaveCount(0)
  })
})
