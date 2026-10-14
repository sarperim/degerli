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

  test('shows the stale macro data type with its last-success date and stale marker', async ({
    page,
  }) => {
    await signIn(page, BUILDER_EMAIL)

    // The seeded universe carries exactly one stale series by construction
    // (GOLD, a daily macro whose last successful ingest was T−10): the API
    // serves `stale: true` for it via the real MacroFreshnessService.
    const summaryResponse = page.waitForResponse(
      (response) => response.url().endsWith('/api/v1/admin/summary') && response.ok(),
    )
    await page.goto('/admin')
    const summary = (await (await summaryResponse).json()) as {
      freshness: Array<{ dataType: string; lastSuccess: string | null; stale: boolean }>
    }
    const staleRow = summary.freshness.find(
      (row) => row.stale && /gold/i.test(row.dataType),
    )
    expect(staleRow).toBeTruthy()
    expect(staleRow?.lastSuccess).toBeTruthy()

    const freshnessTable = page.getByTestId('summary-freshness-table')
    await expect(freshnessTable).toBeVisible()
    const goldRow = freshnessTable.getByRole('row', { name: /gold/i })
    await expect(goldRow).toBeVisible()

    // The last-success date is read from the API (never hardcoded) and rendered
    // locale-aware (formatAsOfDate, tr), so the assertion tracks the now-anchored
    // universe.
    const expectedDate = new Intl.DateTimeFormat('tr', { dateStyle: 'medium' }).format(
      new Date(staleRow!.lastSuccess as string),
    )
    await expect(goldRow).toContainText(expectedDate)

    // The visible stale indication (UXR-MDF-004): copy from the catalog, never
    // hardcoded prose.
    await expect(goldRow.getByText(TR('honestData.stale'))).toBeVisible()
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

/**
 * TC-RES-034 (L4) — admin description review → publish → public effect
 * (`stock-research.md`; UXR-MDF-018..022, FR-RES-020/021, UC-RES-004 steps 2–4,
 * M-2). The FU §9b descriptions: ALFA published, REST draft with both texts,
 * ZETA draft with an empty EN (the publish-gate case).
 *
 * One test owns the whole draft → edited → published → publicly served
 * transition so the mutation is deterministic (playwright `retries: 0`,
 * `workers: 1`; the stack is reseeded per CI run).
 */
test.describe('TC-RES-034 — description review → publish → public effect', () => {
  test('filters the queue, edits both texts with source refs, gates publish on both languages, and serves the published description', async ({
    page,
  }) => {
    await signIn(page, BUILDER_EMAIL)
    await page.goto('/admin')
    await page.getByRole('button', { name: TR('admin.sections.descriptions') }).click()

    // UXR-MDF-018 — the queue lists descriptions filterable by status; the
    // default draft view holds the two seeded drafts.
    const queue = page.getByTestId('descriptions-table')
    await expect(queue.getByRole('row', { name: /REST/ })).toBeVisible()
    await expect(queue.getByRole('row', { name: /ZETA/ })).toBeVisible()

    // Published filter surfaces the published ALFA row.
    await page.getByLabel(TR('admin.descriptions.filterLabel')).selectOption('published')
    await expect(queue.getByRole('row', { name: /ALFA/ })).toBeVisible()
    // Its unresolvable source ref is served as a stub, never dropped (UXR-MDF-020).
    await queue.getByRole('row', { name: /ALFA/ }).getByRole('button', { name: TR('admin.descriptions.edit') }).click()
    const alfaEditor = page.getByTestId('description-editor')
    await expect(alfaEditor.getByTestId('description-source-refs')).toContainText('9999')

    // UXR-MDF-019/020 — the bilingual editor with the KAP source refs alongside.
    await page.getByLabel(TR('admin.descriptions.filterLabel')).selectOption('draft')
    await queue
      .getByRole('row', { name: /REST/ })
      .getByRole('button', { name: TR('admin.descriptions.edit') })
      .click()
    const editor = page.getByTestId('description-editor')
    await expect(editor.getByLabel(TR('admin.descriptions.textTrLabel'))).not.toHaveValue('')
    await expect(editor.getByLabel(TR('admin.descriptions.textEnLabel'))).not.toHaveValue('')
    await expect(editor.getByTestId('description-source-refs')).toContainText(/kap\.org\.tr/)

    // UXR-MDF-019 — editing both languages saves (the editor keeps the saved texts).
    await editor.getByLabel(TR('admin.descriptions.textTrLabel')).fill('Güncel Türkçe açıklama.')
    await editor.getByLabel(TR('admin.descriptions.textEnLabel')).fill('Updated English description.')
    await editor.getByRole('button', { name: TR('admin.descriptions.save') }).click()
    await expect(editor.getByText(TR('admin.descriptions.saved'))).toBeVisible()

    // UXR-MDF-021 — publish is disabled while a language is empty, with the reason.
    await queue
      .getByRole('row', { name: /ZETA/ })
      .getByRole('button', { name: TR('admin.descriptions.edit') })
      .click()
    const zetaEditor = page.getByTestId('description-editor')
    await expect(zetaEditor.getByRole('button', { name: TR('admin.descriptions.publish') })).toBeDisabled()
    await expect(zetaEditor.getByText(TR('admin.descriptions.publishDisabledReason'))).toBeVisible()

    // UXR-MDF-022 — publishing REST requires a confirmation stating public visibility.
    await queue
      .getByRole('row', { name: /REST/ })
      .getByRole('button', { name: TR('admin.descriptions.edit') })
      .click()
    const restEditor = page.getByTestId('description-editor')
    await restEditor.getByRole('button', { name: TR('admin.descriptions.publish') }).click()
    const dialog = page.getByRole('dialog', { name: TR('admin.descriptions.publishTitle') })
    await expect(
      dialog.getByText(TR('admin.descriptions.publishConfirm').replace('{{symbol}}', 'REST')),
    ).toBeVisible()
    await dialog
      .getByRole('button', { name: TR('admin.descriptions.publishConfirmAction') })
      .click()
    await expect(restEditor.getByText(TR('admin.descriptions.publishedNotice'))).toBeVisible()

    // M-2 — the summary's coverage counts update without a manual refresh.
    await page.getByRole('button', { name: TR('admin.sections.summary') }).click()
    await expect(page.getByTestId('coverage-count-published')).toHaveText('2')
    await expect(page.getByTestId('coverage-count-draft')).toHaveText('1')

    // Public effect — the stock's page serves the published description with its
    // last-reviewed date; the "description in preparation" state is gone.
    const overviewResponse = page.waitForResponse(
      (response) => response.url().endsWith('/api/v1/stocks/REST/overview') && response.ok(),
    )
    await page.goto('/stocks/REST')
    const overview = (await (await overviewResponse).json()) as {
      status: string
      lastReviewedAt: string | null
    }
    expect(overview.status).toBe('published')
    expect(overview.lastReviewedAt).toBeTruthy()
    const expectedDate = new Intl.DateTimeFormat('tr', { dateStyle: 'medium' }).format(
      new Date(overview.lastReviewedAt as string),
    )
    const overviewRegion = page.getByRole('region', { name: TR('stockPage.sections.overview') })
    await expect(
      overviewRegion.getByText(TR('stockPage.descriptionLastReviewed').replace('{{date}}', expectedDate)),
    ).toBeVisible()
    await expect(overviewRegion.getByText(TR('honestData.preparingTitle'))).toHaveCount(0)
  })
})
