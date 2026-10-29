import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * TC-SCR-021 (L4) — SCR-012 metric-visibility panel and its consequence-stating
 * confirmation (TKT-scr-005; UXR-MDF-023/024, FR-SCR-017, UC-SCR-004).
 *
 * Against the seeded compose.ci stack: the panel lists the whole metric catalog — the
 * 32 seeded rows (the 18 v1 rows plus TKT-foundation-096's 14 panel rows, 18 screenable
 * in total) — with their state; toggling requires a confirmation stating both consequences;
 * cancel aborts with no change; confirming flips the state and SCR-003's criteria
 * builder reflects it on the next load without a manual refresh. The metric is
 * restored at the end so the shared stack is left as seeded.
 *
 * Copy is read from the checked-in catalogs (§8.3); the seeded metric label is data,
 * not UI prose.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

const BUILDER_EMAIL = 'builder@degerli.test'
const FIXTURE_PASSWORD = 'FixturePass1!'

/** The seeded ROE label (`DegerliSeed.MetricCatalogSql`), the panel row under test. */
const ROE_LABEL = 'Özkaynak Kârlılığı'

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

async function openMetricPanel(page: import('@playwright/test').Page) {
  await page.goto('/admin')
  await page.getByRole('button', { name: TR('admin.sections.metrics') }).click()
  const table = page.getByTestId('metric-visibility-table')
  await expect(table).toBeVisible()
  return table
}

test.describe('TC-SCR-021 — metric visibility panel', () => {
  test('lists metrics, confirms the consequences, toggles, and SCR-003 reflects it', async ({
    page,
  }) => {
    await signIn(page, BUILDER_EMAIL)

    let table = await openMetricPanel(page)

    try {

      // The 32 catalog metrics with their state (UXR-MDF-023): the panel lists the whole
      // catalog, not only the screenable rows — a metric has to be listed to be toggled. The
      // catalog is the 18 v1 rows plus TKT-foundation-096's 14 panel rows, seeded with
      // `is_screenable = false` (the screener's offered set is unchanged until a builder flips
      // them); the old constant was bound to the 18-row catalog.
      await expect(table.getByRole('row')).toHaveCount(33) // header + 32 metrics
      let roeRow = table.getByRole('row', { name: new RegExp(ROE_LABEL) })
      await expect(roeRow).toContainText(TR('admin.metrics.screenable'))

      // Toggling requires a confirmation that states both consequences (UXR-MDF-024).
      await roeRow.getByRole('button', { name: TR('admin.metrics.hide') }).click()
      const dialog = page.getByRole('dialog')
      await expect(dialog.getByText(TR('admin.metrics.confirmDescription'))).toBeVisible()

      // Cancel aborts with no change.
      await dialog.getByRole('button', { name: TR('common.cancel') }).click()
      await expect(roeRow).toContainText(TR('admin.metrics.screenable'))

      // Confirm → the metric is hidden and the panel reflects it.
      await roeRow.getByRole('button', { name: TR('admin.metrics.hide') }).click()
      await page
        .getByRole('dialog')
        .getByRole('button', { name: TR('admin.metrics.confirmHide') })
        .click()
      await expect(roeRow).toContainText(TR('admin.metrics.hidden'))

      // SCR-003's criteria builder stops offering it on next load (no manual refresh).
      await page.goto('/screener')
      const metricSelect = page.getByTestId('metric-select')
      await expect(metricSelect).toBeVisible()
      await expect(metricSelect.locator('option', { hasText: ROE_LABEL })).toHaveCount(0)
    } finally {
      // Restore the seeded visibility so the shared stack is left clean.
      table = await openMetricPanel(page)
      const roeRow = table.getByRole('row', { name: new RegExp(ROE_LABEL) })
      if ((await roeRow.textContent())?.includes(TR('admin.metrics.hidden'))) {
        await roeRow.getByRole('button', { name: TR('admin.metrics.show') }).click()
        await page
          .getByRole('dialog')
          .getByRole('button', { name: TR('admin.metrics.confirmShow') })
          .click()
      }
      await expect(roeRow).toContainText(TR('admin.metrics.screenable'))
    }
  })
})
