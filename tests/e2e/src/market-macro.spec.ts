import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'
import { assertNetworkQuiet, recordRequests } from './helpers/network'

/**
 * TC-MOV-016 (macro-strip half), TC-MOV-017, TC-MOV-022, TC-MOV-023 and TC-MOV-024
 * (`.pipeline/testing/market-overview.md` §2 Group D) — the SCR-001 macro strip
 * end-to-end against the seeded compose.ci stack, anonymous.
 *
 * TC-MOV-016 (macro half): all six indicators render with value, unit, plain-language
 * label, source attribution and as-of date (UXR-MOV-007). TC-MOV-017: the official
 * and independent inflation measures are always both visible, each with its source
 * and as-of date, and the pair survives a narrow viewport. TC-MOV-022: over a
 * 3-second quiet window measured after load, no periodic requests hit /market/*; the
 * freshness is framed as EOD with an as-of date. TC-MOV-023: the language toggle
 * re-renders in English with the same data (no refetch), the same scroll position,
 * and back. TC-MOV-024: the always-seeded stale GOLD series carries the stale marker
 * together with its as-of date while a fresh series carries the as-of only.
 *
 * Copy is read from the checked-in catalogs (§8.3); macro indicator values are
 * asserted structurally (present, non-empty) rather than as hardcoded prose.
 */

const TR = loadCatalog('tr')
const EN = loadCatalog('en')
const tr = (key: string) => catalogValue(TR, key)
const en = (key: string) => catalogValue(EN, key)

const SERIES_CODES = [
  'TUIK_CPI',
  'INDEP_CPI',
  'CBRT_REPO',
  'USD_TRY',
  'EUR_TRY',
  'GOLD',
] as const

test.describe('TC-MOV-016 macro strip renders six indicators with value/unit/source/as-of', () => {
  test('every indicator shows its label, value, unit, source and as-of date', async ({
    page,
  }) => {
    await page.goto('/')

    await expect(page.getByTestId('market-block-macro')).toBeVisible()
    await expect(
      page.getByRole('heading', { level: 2, name: tr('marketOverview.macro.title') }),
    ).toBeVisible()

    for (const code of SERIES_CODES) {
      const indicator = page.getByTestId(`macro-${code}`)
      await expect(indicator).toBeVisible()
      await expect(indicator).toContainText(tr(`marketOverview.macro.series.${code}`))
      await expect(page.getByTestId(`macro-value-${code}`)).not.toBeEmpty()
      await expect(page.getByTestId(`macro-unit-${code}`)).not.toBeEmpty()
      await expect(page.getByTestId(`macro-source-${code}`)).not.toBeEmpty()
      await expect(indicator.locator('[data-slot="as-of-date"]')).toBeVisible()
    }
  })
})

test.describe('TC-MOV-017 inflation pair displayed side by side', () => {
  test('both measures are visible with source and as-of, and survive a narrow viewport', async ({
    page,
  }) => {
    await page.goto('/')

    const pair = page.getByTestId('macro-inflation-pair')
    await expect(pair).toBeVisible()

    const official = pair.getByTestId('macro-TUIK_CPI')
    const independent = pair.getByTestId('macro-INDEP_CPI')
    await expect(official).toBeVisible()
    await expect(independent).toBeVisible()
    await expect(official.locator('[data-slot="as-of-date"]')).toBeVisible()
    await expect(independent.locator('[data-slot="as-of-date"]')).toBeVisible()
    await expect(page.getByTestId('macro-source-TUIK_CPI')).toContainText('TÜİK')
    await expect(page.getByTestId('macro-source-INDEP_CPI')).toContainText('ENAG')

    // Narrow viewport (UXR-G-025): the pair may stack but both stay visible.
    await page.setViewportSize({ width: 360, height: 800 })
    await expect(pair.getByTestId('macro-TUIK_CPI')).toBeVisible()
    await expect(pair.getByTestId('macro-INDEP_CPI')).toBeVisible()
  })
})

test.describe('TC-MOV-024 stale marker component', () => {
  test('a stale series shows the badge with its as-of while a fresh series shows as-of only', async ({
    page,
  }) => {
    await page.goto('/')

    // The seeded GOLD series is last-known-good (stale).
    const gold = page.getByTestId('macro-GOLD')
    await expect(gold).toBeVisible()
    await expect(gold.locator('[data-slot="stale-badge"]')).toBeVisible()
    await expect(gold.locator('[data-slot="as-of-date"]')).toBeVisible()

    // A fresh series shows the as-of date but no stale badge.
    const usd = page.getByTestId('macro-USD_TRY')
    await expect(usd.locator('[data-slot="as-of-date"]')).toBeVisible()
    await expect(usd.locator('[data-slot="stale-badge"]')).toHaveCount(0)
  })
})

test.describe('TC-MOV-022 no real-time implication', () => {
  test('no periodic /market/* requests during a 3s quiet window after load', async ({
    page,
  }) => {
    await page.goto('/')
    await expect(page.getByTestId('market-block-macro')).toBeVisible()

    // The quiet window must be measured after the initial data load has settled,
    // not from the navigation start (TKT-mov-006; the foundation-010 harness
    // started its window at `goto` and so caught the first render's fetches).
    await page.waitForLoadState('networkidle')
    await assertNetworkQuiet(page, {
      windowMs: 3_000,
      match: /\/api\/v1\/market\//,
    })

    // Freshness is framed as EOD with an as-of date and no countdown/live control.
    await expect(page.locator('[data-slot="as-of-date"]').first()).toBeVisible()
    await expect(page.getByTestId('macro-eod-note')).toBeVisible()
  })
})

test.describe('TC-MOV-023 language switch preserves context', () => {
  test('TR→EN re-renders the macro strip with the same data, scroll and back to TR', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 400 })
    await page.goto('/')
    await expect(page.getByTestId('market-block-macro')).toBeVisible()

    await page.evaluate(() => window.scrollTo(0, 300))
    const scrollBefore = await page.evaluate(() => window.scrollY)
    expect(scrollBefore).toBeGreaterThan(0)

    // Toggling language must not refetch macro data — same data from cache.
    const recorder = recordRequests(page, /\/api\/v1\/market\/macro/)

    // Invoke the toggle's click handler directly: Playwright's `click` would scroll
    // the (non-sticky) header back into view first, masking whether the language
    // switch itself preserves the scroll position.
    await page
      .getByRole('group', { name: tr('header.languageToggle') })
      .getByRole('button', { name: tr('header.english') })
      .evaluate((element) => (element as HTMLElement).click())

    await expect(
      page.getByRole('heading', { level: 2, name: en('marketOverview.macro.title') }),
    ).toBeVisible()
    await expect(page.getByTestId('market-block-macro')).toBeVisible()
    expect(recorder.stop()).toEqual([])

    // Same data, same scroll position.
    await expect(page.getByTestId('macro-TUIK_CPI')).toContainText('45')
    const scrollAfter = await page.evaluate(() => window.scrollY)
    expect(Math.abs(scrollAfter - scrollBefore)).toBeLessThanOrEqual(1)

    // Back to Turkish.
    await page
      .getByRole('group', { name: en('header.languageToggle') })
      .getByRole('button', { name: en('header.turkish') })
      .evaluate((element) => (element as HTMLElement).click())
    await expect(
      page.getByRole('heading', { level: 2, name: tr('marketOverview.macro.title') }),
    ).toBeVisible()
  })
})
