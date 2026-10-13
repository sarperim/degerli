import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * SCR-012 Admin Dashboard (L4) — TKT-mdf-012.
 *
 * Against the seeded compose.ci stack (now-anchored fixture universe): the
 * builder-only entry and access-denied state (TC-MDF-045), the ops summary
 * structure (TC-MDF-046), the healthy empty quarantine state (TC-MDF-050) and
 * the coverage report's instrument/fund scope switch with the seeded TEF0002
 * holdings gap (TC-MDF-051, TC-FDF-012).
 *
 * Copy is read from the checked-in catalogs (§8.3) so assertions never hardcode
 * prose.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

/** A verified builder fixture account (`fixtures/Degerli.Fixtures/FixtureUniverse.cs`). */
const BUILDER_EMAIL = 'builder@degerli.test'
const REGULAR_EMAIL = 'user-a@degerli.test'
const FIXTURE_PASSWORD = 'FixturePass1!'

// The API's /auth/* rate-limit budget is 5/min keyed by client IP; tag each
// browser request with its own X-Forwarded-For, as the L2 integration tests do.
let ipCounter = 0
function nextForwardedFor(): string {
  ipCounter += 1
  return `198.51.100.${ipCounter}`
}

async function signIn(page: import('@playwright/test').Page, email: string) {
  await page.route('**/api/v1/**', (route) =>
    route.continue({
      headers: { ...route.request().headers(), 'X-Forwarded-For': nextForwardedFor() },
    }),
  )
  await page.goto('/sign-in')
  await page.getByLabel(TR('signIn.emailLabel')).fill(email)
  await page.getByLabel(TR('signIn.passwordLabel')).fill(FIXTURE_PASSWORD)
  await page
    .getByRole('form', { name: TR('signIn.formAriaLabel') })
    .getByRole('button', { name: TR('signIn.submit') })
    .click()
  await expect(page.getByRole('button', { name: TR('header.signOut') })).toBeVisible()
}

test.describe('TC-MDF-045 — admin entry visibility and access-denied state', () => {
  test('a builder sees the admin nav entry and the admin sections', async ({ page }) => {
    await signIn(page, BUILDER_EMAIL)

    const nav = page.getByRole('navigation', { name: TR('header.primaryNav') })
    await expect(nav.getByRole('link', { name: TR('nav.admin') })).toBeVisible()

    await nav.getByRole('link', { name: TR('nav.admin') }).click()
    await expect(page).toHaveURL(/\/admin$/)
    await expect(
      page.getByRole('navigation', { name: TR('admin.sectionsNavLabel') }),
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { name: TR('admin.summary.jobsHeading') }),
    ).toBeVisible()
  })

  test('a regular account gets the plain-language denied state, not a blank page', async ({
    page,
  }) => {
    await signIn(page, REGULAR_EMAIL)
    await page.goto('/admin')
    await expect(
      page.getByRole('heading', { name: TR('admin.accessDenied.title') }),
    ).toBeVisible()
    await expect(
      page.getByRole('link', { name: TR('admin.accessDenied.back') }),
    ).toBeVisible()
  })

  test('an anonymous visitor gets the denied state on the direct URL', async ({ page }) => {
    await page.goto('/admin')
    await expect(
      page.getByRole('heading', { name: TR('admin.accessDenied.title') }),
    ).toBeVisible()
  })
})

test.describe('TC-MDF-046 — ops summary renders', () => {
  test('shows the per-job, per-data-type and coverage summary structure', async ({ page }) => {
    await signIn(page, BUILDER_EMAIL)
    await page.goto('/admin')

    await expect(
      page.getByRole('heading', { name: TR('admin.summary.jobsHeading') }),
    ).toBeVisible()
    // Every registered job is listed with a last-run cell (never-run is a value).
    await expect(
      page.getByTestId('summary-jobs-table').getByRole('row', { name: /prices/ }),
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { name: TR('admin.summary.freshnessHeading') }),
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { name: TR('admin.summary.contentHeading') }),
    ).toBeVisible()
    await expect(page.getByTestId('coverage-count-universe')).toBeVisible()

    // The open-quarantine count links into the queue (UXR-MDF-006).
    await expect(page.getByTestId('open-quarantine-count')).toBeVisible()
    await page.getByRole('button', { name: TR('admin.summary.openQuarantineLink') }).click()
    await expect(
      page.getByRole('heading', { name: TR('admin.sections.quarantine') }),
    ).toBeVisible()
  })
})

test.describe('TC-MDF-050 — admin empty states', () => {
  test('the open quarantine queue names the healthy condition', async ({ page }) => {
    await signIn(page, BUILDER_EMAIL)
    await page.goto('/admin')
    await page
      .getByRole('button', { name: TR('admin.sections.quarantine') })
      .click()
    await expect(page.getByText(TR('admin.quarantine.emptyOpenTitle'))).toBeVisible()
  })

  test('the run ledger names the filter-matching-nothing condition', async ({ page }) => {
    await signIn(page, BUILDER_EMAIL)
    await page.goto('/admin')
    await page.getByRole('button', { name: TR('admin.sections.ledger') }).click()

    // A job/status filter that matches no seeded run surfaces the named empty
    // state, never a blank pane (UXR-MDF-027).
    await page.getByLabel(TR('admin.ledger.filterJob')).selectOption('medians')
    await page.getByLabel(TR('admin.ledger.filterStatus')).selectOption('failed')

    await expect(page.getByText(TR('admin.ledger.emptyTitle'))).toBeVisible()
    await expect(page.getByText(TR('admin.ledger.emptyBody'))).toBeVisible()
  })
})

test.describe('TC-MDF-051 / TC-FDF-012 — coverage report & scope switch', () => {
  test('switches to fund scope and shows the funds with TEF0002 holdings gap', async ({
    page,
  }) => {
    await signIn(page, BUILDER_EMAIL)
    await page.goto('/admin')
    await page.getByRole('button', { name: TR('admin.sections.coverage') }).click()

    // Instrument scope renders the covered universe with listing dates.
    await expect(page.getByTestId('coverage-instrument-table')).toBeVisible()
    await expect(page.getByRole('row', { name: /PART/ })).toBeVisible()

    // Fund scope: the three seeded funds, TEF0002's holdings gap explicit.
    await page.getByRole('radio', { name: TR('admin.coverage.scopeFund') }).click()
    const fundTable = page.getByTestId('coverage-fund-table')
    await expect(fundTable).toBeVisible()
    for (const code of ['TEF0001', 'TEF0002', 'TEF0003']) {
      await expect(fundTable.getByRole('row', { name: new RegExp(code) })).toBeVisible()
    }
    const tef0002 = fundTable.getByRole('row', { name: /TEF0002/ })
    const holdingsEntry = tef0002
      .getByRole('listitem')
      .filter({ hasText: TR('admin.dataTypes.holdings') })
    await expect(holdingsEntry).toBeVisible()
    await expect(holdingsEntry.getByText(TR('admin.coverage.gap'))).toBeVisible()
  })
})
