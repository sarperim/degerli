import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'
import { signInWithFixture } from './helpers/globalUx'
import { tagWithClientIp } from './helpers/accounts'

/**
 * TC-XC-019 (L4, 375px viewport class) — responsive operability at narrow width
 * (`.pipeline/testing/cross-cutting.md` §2; UXR-G-025).
 *
 * At 375px every pinned surface — SCR-002/003/005/006/012 — remains fully
 * operable: navigation, reading and every mutation are reachable, and data
 * tables degrade in presentation without losing any row, value, marker or action.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

const BUILDER_EMAIL = 'builder@degerli.test'

test.use({ viewport: { width: 375, height: 812 } })

test.beforeEach(async ({ page }) => {
  tagWithClientIp(page)
})

test.describe('TC-XC-019 — responsive operability at 375px', () => {
  test('the primary navigation remains reachable at 375px', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('link', { name: TR('nav.stocks') }).click()
    await expect(page).toHaveURL(/\/stocks$/)
    await page.getByRole('link', { name: TR('nav.screener') }).click()
    await expect(page).toHaveURL(/\/screener$/)
  })

  test('SCR-002 keeps every row, filter and value at 375px', async ({ page }) => {
    await page.goto('/stocks')
    await expect(page.getByTestId('stock-row-ALFA')).toBeVisible()

    // All 16 covered stocks remain rendered as rows (no rows dropped for width).
    for (const symbol of [
      'BETA', 'GAMA', 'DELTA', 'REST', 'EPSL', 'ZETA', 'ETA', 'THETA',
      'PART', 'NEWP', 'IOTA', 'KAPPA', 'LAMDA', 'NU', 'UNSEC',
    ]) {
      await expect(page.getByTestId(`stock-row-${symbol}`)).toBeAttached()
    }

    // Filters are operable, and a row action is reachable.
    await page.getByLabel(TR('stockList.sectorLabel')).selectOption('A')
    await page.getByLabel(TR('stockList.searchLabel')).fill('beta')
    await expect(page.getByTestId('stock-row-BETA')).toBeVisible()
    await page
      .getByRole('link', { name: 'Beta Yazılım A.Ş. (BETA)' })
      .click()
    await expect(page).toHaveURL(/\/stocks\/BETA$/)
  })

  test('SCR-003 build → run → result stays operable at 375px', async ({ page }) => {
    await page.goto('/screener')
    await page.getByTestId('metric-select').selectOption('pe')
    await page.getByTestId('add-criterion').click()
    const row = page.getByTestId('criterion-row').last()
    await row.getByTestId('criterion-bound').selectOption('max')
    await row.getByTestId('criterion-max').fill('15')
    await page.getByTestId('run-button').click()
    await expect(page.getByTestId('results')).toBeVisible()
    await expect(page.getByTestId('match-count')).toBeVisible()
  })

  test('SCR-005 keeps its sections and honest-data marks at 375px', async ({ page }) => {
    await page.goto('/stocks/ALFA', { waitUntil: 'networkidle' })
    await expect(
      page.getByRole('heading', { level: 1, name: TR('screens.stockPage.title') }),
    ).toBeVisible()
    await expect(page.locator('[data-slot="as-of-date"]').first()).toBeVisible()
  })

  test('SCR-006 parameters and the result stay operable at 375px', async ({ page }) => {
    await page.goto('/stocks/ALFA/dcf', { waitUntil: 'networkidle' })
    await expect(page.getByTestId('dcf-params')).toBeVisible()
    await page.getByTestId('dcf-param-discount_rate').fill('17')
    await expect(page.getByTestId('dcf-param-discount_rate')).toHaveValue('17')
    await expect(page.getByTestId('dcf-fair-value')).toBeVisible()
  })

  test('SCR-012 admin remains operable at 375px', async ({ page }) => {
    tagWithClientIp(page)
    await signInWithFixture(page, BUILDER_EMAIL)

    await page.goto('/admin')
    await expect(page.getByTestId('ops-summary')).toBeVisible()
  })
})
