import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * SCR-012 DCF baseline regeneration section (L4) — TKT-val-009, TC-VAL-016.
 *
 * Against the seeded compose.ci stack: the builder triggers DCF baseline
 * regeneration behind a confirmation stating that baselines are rebuilt from
 * canonical facts and versioned (UXR-MDF-025). Cancel aborts with no run; on
 * confirm the run becomes visible in the run ledger and SCR-006 serves the new
 * active baseline on its next load.
 *
 * Copy is read from the checked-in catalogs (§8.3) so assertions never hardcode
 * prose.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

/** A verified builder fixture account (`fixtures/Degerli.Fixtures/FixtureUniverse.cs`). */
const BUILDER_EMAIL = 'builder@degerli.test'
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

/** The active baseline version served for ALFA, or null when none is active yet. */
async function baselineVersion(page: import('@playwright/test').Page): Promise<number | null> {
  return page.evaluate(async () => {
    const response = await fetch('/api/v1/stocks/ALFA/dcf', {
      headers: { 'X-Forwarded-For': `198.51.100.${Math.floor(Math.random() * 200) + 1}` },
    })
    if (!response.ok) return null
    const body = (await response.json()) as { baseline?: { version?: number } }
    return body.baseline?.version ?? null
  })
}

/** The number of `dcf-baselines` runs recorded in the ledger. */
async function baselineRunCount(page: import('@playwright/test').Page): Promise<number> {
  return page.evaluate(async () => {
    const response = await fetch('/api/v1/admin/ingest-runs?job=dcf-baselines', {
      headers: { 'X-Forwarded-For': `198.51.100.${Math.floor(Math.random() * 200) + 1}` },
    })
    const body = (await response.json()) as { runs: unknown[] }
    return body.runs.length
  })
}

test.describe('TC-VAL-016 — DCF baseline regeneration confirmation', () => {
  test('confirmation gates regeneration; a confirmed run is ledger-visible and bumps the active baseline', async ({
    page,
  }) => {
    await signIn(page, BUILDER_EMAIL)

    await page.goto('/admin')
    await page.getByRole('button', { name: TR('admin.sections.dcfBaselines') }).click()
    await expect(page.getByTestId('admin-dcf-baselines')).toBeVisible()
    await expect(page.getByText(TR('admin.dcfBaselines.explanation'))).toBeVisible()

    const runsBefore = await baselineRunCount(page)
    const versionBefore = await baselineVersion(page)

    // The action requires a confirmation stating rebuild-from-canonical-facts +
    // versioning; cancel aborts, no run.
    await page.getByRole('button', { name: TR('admin.dcfBaselines.regenerate') }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    await expect(
      dialog.getByRole('heading', { name: TR('admin.dcfBaselines.confirmTitle') }),
    ).toBeVisible()
    await expect(dialog.getByText(TR('admin.dcfBaselines.confirmDescription'))).toBeVisible()

    await dialog.getByRole('button', { name: TR('admin.dcfBaselines.cancel') }).click()
    await expect(dialog).not.toBeVisible()
    expect(await baselineRunCount(page)).toBe(runsBefore)
    expect(await baselineVersion(page)).toBe(versionBefore)

    // On confirm the run is visible in the ledger and the active version bumps.
    await page.getByRole('button', { name: TR('admin.dcfBaselines.regenerate') }).click()
    await page.getByRole('dialog').getByRole('button', { name: TR('admin.dcfBaselines.confirm') }).click()
    await expect(page.getByTestId('dcf-baselines-success')).toBeVisible()

    await page.getByRole('button', { name: TR('admin.sections.ledger') }).click()
    await expect(
      page.getByTestId('ledger-table').getByRole('row', { name: /dcf-baselines/ }),
    ).toBeVisible()

    await expect.poll(() => baselineRunCount(page)).toBe(runsBefore + 1)
    const versionAfter = await baselineVersion(page)
    expect(versionAfter).not.toBeNull()
    expect(versionAfter).toBe((versionBefore ?? 0) + 1)

    // SCR-006 serves the new active baseline on its next load.
    await page.goto('/stocks/ALFA/dcf')
    await expect(page.getByTestId('dcf-baseline-version')).toContainText(String(versionAfter))
  })
})
