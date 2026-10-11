import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * TC-MOV-016 / TC-MOV-019 / TC-MOV-020 / TC-MOV-021 (L4) — the SCR-001 Market
 * Overview equity blocks, navigation and disclaimer against the seeded
 * compose.ci stack, end-to-end and anonymous
 * (`.pipeline/testing/market-overview.md` §2 Group D).
 *
 * TC-MOV-016: an anonymous visit to the root renders every equity block with
 * values, change direction is conveyed non-colour-only, the informational-only
 * disclaimer is visible, the initial language is Turkish with the toggle present,
 * and the equity data-as-of date is displayed. (The macro-strip half of
 * TC-MOV-016 — the six indicators with unit/source/as-of — is asserted by
 * TKT-mov-006.)
 * TC-MOV-019: a gainer entry opens that stock's page; a sector entry opens the
 * Stock List pre-filtered; the Screener and Stock List are reachable from the
 * primary navigation.
 * TC-MOV-020: every mover entry displays its traded volume.
 * TC-MOV-021: the market-valuation block shows the loss-maker exclusion
 * disclosure with the excluded count.
 *
 * Copy is read from the checked-in catalogs (§8.3) so assertions never hardcode
 * prose; dates are never asserted (now-anchored fixture offsets only).
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

test.describe('TC-MOV-016 dashboard renders anonymously, TR default, equity blocks', () => {
  test('renders the equity blocks with values, disclaimer, TR default and as-of', async ({
    page,
  }) => {
    await page.goto('/')

    // Anonymous + Turkish default with the language toggle available.
    await expect(page.getByText(TR('header.anonymous'))).toBeVisible()
    await expect(
      page.getByRole('heading', { level: 1, name: TR('screens.marketOverview.title') }),
    ).toBeVisible()
    await expect(
      page
        .getByRole('group', { name: TR('header.languageToggle') })
        .getByRole('button', { name: TR('header.turkish') }),
    ).toHaveAttribute('aria-pressed', 'true')

    // Index levels with a directional (signed) daily change.
    const xu100 = page.getByTestId('index-xu100')
    await expect(xu100).toBeVisible()
    await expect(xu100).toContainText(TR('marketOverview.blocks.indices.xu100'))
    await expect(xu100).toContainText(/[+−-]/)
    await expect(page.getByTestId('index-xu30')).toBeVisible()

    // Sector performance, breadth, volume, valuation.
    await expect(page.getByTestId('sector-entry-A')).toBeVisible()
    await expect(page.getByTestId('breadth-advancing')).toBeVisible()
    await expect(page.getByTestId('market-volume-total')).toBeVisible()
    await expect(page.getByTestId('market-pe')).toBeVisible()

    // Equity data-as-of date displayed.
    await expect(page.locator('[data-slot="as-of-date"]').first()).toBeVisible()

    // Informational-only disclaimer visible (shell footer for research routes).
    await expect(page.locator('[data-slot="disclaimer"]')).toBeVisible()
  })
})

test.describe('TC-MOV-019 dashboard navigation click-throughs', () => {
  test('a gainer entry opens that stock page', async ({ page }) => {
    await page.goto('/')

    await page.getByTestId('mover-gainer-ETA').click()
    await expect(page).toHaveURL(/\/stocks\/ETA$/)
  })

  test('a sector entry opens the Stock List pre-filtered to that sector', async ({
    page,
  }) => {
    await page.goto('/')

    await page.getByTestId('sector-entry-A').click()
    await expect(page).toHaveURL(/\/stocks\?sector=A$/)
    await expect(page.getByTestId('active-sector')).toContainText('A')
  })

  test('the Screener and Stock List are reachable from the navigation', async ({
    page,
  }) => {
    await page.goto('/')

    await page.getByRole('link', { name: TR('nav.stocks') }).click()
    await expect(page).toHaveURL(/\/stocks$/)

    await page.goto('/')
    await page.getByRole('link', { name: TR('nav.screener') }).click()
    await expect(page).toHaveURL(/\/screener$/)
  })
})

test.describe('TC-MOV-020 volume shown per mover entry', () => {
  test('each gainer and loser entry displays its traded volume', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByTestId('mover-volume-ETA')).toBeVisible()
    await expect(page.getByTestId('mover-volume-ETA')).not.toBeEmpty()
    await expect(page.getByTestId('mover-volume-THETA')).toBeVisible()
    await expect(page.getByTestId('mover-volume-THETA')).not.toBeEmpty()
  })
})

test.describe('TC-MOV-021 loss-maker exclusion disclosure rendered', () => {
  test('the market-valuation block discloses excluded loss-makers with the count', async ({
    page,
  }) => {
    await page.goto('/')

    const disclosure = page.getByTestId('market-pe-disclosure')
    await expect(disclosure).toBeVisible()
    // The count is interpolated from the payload (fixture goldens: 2).
    await expect(disclosure).toContainText('2')
  })
})
