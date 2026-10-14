import { expect, test, type Page } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'
import { signInWithFixture, switchLanguage } from './helpers/globalUx'
import { assertNetworkQuiet, recordRequests } from './helpers/network'
import { tagWithClientIp } from './helpers/accounts'

/**
 * Global UX behaviour sweep (TKT-int-003) — language, disclaimers and no-real-time
 * framing, L4 against the full compose.ci stack.
 *
 * - TC-XC-003 — Turkish default on first visit (pristine-device class).
 * - TC-XC-004 — language switch preserves context on data-bearing screens.
 * - TC-XC-005 — language persistence: anonymous device legs + account preference
 *   applied at the next sign-in.
 * - TC-XC-006 — informational-only disclaimer across the seven research surfaces.
 * - TC-XC-021 — no real-time framing / absence of polling on market-data surfaces.
 *
 * Copy is read from the checked-in catalogs (§8.3); dates are never asserted.
 * Every request is tagged with its own client IP (I-XC-2) so the shared global
 * rate-limit bucket never throttles a later test in the same file.
 */

const tr = loadCatalog('tr')
const en = loadCatalog('en')
const TR = (key: string) => catalogValue(tr, key)
const EN = (key: string) => catalogValue(en, key)

const VERIFIED_USER = 'user-a@degerli.test'

test.beforeEach(async ({ page }) => {
  tagWithClientIp(page)
})

/* ------------------------------------------------------------------ *
 * TC-XC-003 — Turkish default on first visit
 * ------------------------------------------------------------------ */

const TR_DEFAULT_SURFACES: Array<{ path: string; titleKey: string }> = [
  { path: '/', titleKey: 'screens.marketOverview.title' },
  { path: '/stocks', titleKey: 'screens.stockList.title' },
  { path: '/screener', titleKey: 'screens.screener.title' },
  { path: '/stocks/ALFA', titleKey: 'screens.stockPage.title' },
  { path: '/stocks/ALFA/dcf', titleKey: 'screens.dcfCalculator.title' },
  { path: '/register', titleKey: 'screens.register.title' },
]

test.describe('TC-XC-003 — Turkish default on first visit', () => {
  test('a pristine browser context renders every surface in Turkish', async ({
    browser,
  }) => {
    const context = await browser.newContext()
    const page = await context.newPage()
    tagWithClientIp(page)

    for (const surface of TR_DEFAULT_SURFACES) {
      await page.goto(surface.path)
      await expect(page.locator('html')).toHaveAttribute('lang', 'tr')
      await expect(
        page.getByRole('heading', { level: 1, name: TR(surface.titleKey) }),
      ).toBeVisible()
      // The toggle is present and Turkish is the pressed option.
      await expect(
        page
          .getByRole('group', { name: TR('header.languageToggle') })
          .getByRole('button', { name: TR('header.turkish') }),
      ).toHaveAttribute('aria-pressed', 'true')
    }

    await context.close()
  })

  test('the localStorage leg overrides the TR default (account leg not exercised)', async ({
    browser,
  }) => {
    const context = await browser.newContext()
    // Resolution order is account-pref > localStorage > tr; with no account this
    // pristine context exercises the localStorage leg winning over the default.
    await context.addInitScript(() => {
      window.localStorage.setItem('degerli.language', 'en')
    })
    const page = await context.newPage()
    tagWithClientIp(page)

    await page.goto('/')
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')

    await context.close()
  })
})

/* ------------------------------------------------------------------ *
 * TC-XC-004 — language switch preserves context across data-bearing screens
 * ------------------------------------------------------------------ */

test.describe('TC-XC-004 — language switch preserves context', () => {
  test('SCR-002 keeps the sector filter, search term, scroll and data cache', async ({
    page,
  }) => {
    await page.goto('/stocks')
    await expect(page.getByTestId('stock-row-ALFA')).toBeVisible()

    await page.getByLabel(TR('stockList.sectorLabel')).selectOption('A')
    await page.getByLabel(TR('stockList.searchLabel')).fill('beta')
    await expect(page.getByTestId('stock-row-BETA')).toBeVisible()
    await expect(page.getByTestId('active-sector')).toContainText('A')
    await expect(page.getByTestId('active-search')).toContainText('beta')

    // Put the viewport mid-page so a switch that reset to the top would be caught.
    await page.evaluate(() => window.scrollTo(0, 250))
    const scrollBefore = await page.evaluate(() => window.scrollY)

    // Switching must not re-fetch the data caches (only the session read is exempt).
    const recorder = recordRequests(page, /\/api\/v1\/(?!auth\/session)/)
    await switchLanguage(page, 'en')

    // Same URL, same screen, in the other language.
    await expect(page).toHaveURL(/\/stocks$/)
    await expect(
      page.getByRole('heading', { level: 1, name: EN('screens.stockList.title') }),
    ).toBeVisible()

    // Filters, search and rows survive unchanged.
    await expect(page.getByLabel(EN('stockList.searchLabel'))).toHaveValue('beta')
    await expect(page.getByLabel(EN('stockList.sectorLabel'))).toHaveValue('A')
    await expect(page.getByTestId('active-sector')).toContainText('A')
    await expect(page.getByTestId('stock-row-BETA')).toBeVisible()

    expect(recorder.stop()).toEqual([])

    const scrollAfter = await page.evaluate(() => window.scrollY)
    expect(Math.abs(scrollAfter - scrollBefore)).toBeLessThan(60)
  })

  test('SCR-003 keeps built criteria and values', async ({ page }) => {
    await page.goto('/screener')
    await page.getByTestId('metric-select').selectOption('pe')
    await page.getByTestId('add-criterion').click()
    const row = page.getByTestId('criterion-row').last()
    await row.getByTestId('criterion-bound').selectOption('max')
    await row.getByTestId('criterion-max').fill('15')

    await switchLanguage(page, 'en')

    await expect(page).toHaveURL(/\/screener$/)
    await expect(
      page.getByRole('heading', { level: 1, name: EN('screens.screener.title') }),
    ).toBeVisible()
    await expect(page.getByTestId('criterion-row')).toHaveCount(1)
    await expect(page.getByTestId('criterion-max')).toHaveValue('15')
  })

  test('SCR-006 keeps modified DCF assumptions', async ({ page }) => {
    await page.goto('/stocks/ALFA/dcf')
    await expect(page.getByTestId('dcf-params')).toBeVisible()
    await page.getByTestId('dcf-param-discount_rate').fill('17')
    await expect(page.getByTestId('dcf-param-discount_rate')).toHaveValue('17')

    await switchLanguage(page, 'en')

    await expect(page).toHaveURL(/\/stocks\/ALFA\/dcf$/)
    await expect(
      page.getByRole('heading', {
        level: 1,
        name: EN('screens.dcfCalculator.title'),
      }),
    ).toBeVisible()
    await expect(page.getByTestId('dcf-param-discount_rate')).toHaveValue('17')
  })
})

/* ------------------------------------------------------------------ *
 * TC-XC-005 — language persistence (device and account)
 * ------------------------------------------------------------------ */

test.describe('TC-XC-005 — language persistence', () => {
  test('an anonymous EN choice survives a reload and a new visit on the same device', async ({
    browser,
  }) => {
    const context = await browser.newContext()
    const page = await context.newPage()
    tagWithClientIp(page)

    await page.goto('/')
    await switchLanguage(page, 'en')
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')

    // Same device, same tab: survives a reload.
    await page.reload()
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')

    // Same device, a new visit (new page, same context/storage).
    const revisit = await context.newPage()
    tagWithClientIp(revisit)
    await revisit.goto('/')
    await expect(revisit.locator('html')).toHaveAttribute('lang', 'en')

    await context.close()
  })

  test('the account preference applies at the next sign-in on another device', async ({
    browser,
  }) => {
    // Device A: sign in and set English as the account preference.
    const contextA = await browser.newContext()
    const pageA = await contextA.newPage()
    tagWithClientIp(pageA)
    await signInWithFixture(pageA, VERIFIED_USER)
    await pageA.goto('/settings')
    // Normalize to Turkish first: the seeded account's stored preference is not
    // guaranteed after sibling specs, and the toggle labels are read from it.
    await switchLanguage(pageA, 'tr')
    const group = pageA.getByRole('group', {
      name: TR('accountSettings.languageLabel'),
    })
    await group.getByRole('button', { name: TR('header.english') }).click()
    await expect(pageA.getByTestId('language-preference')).toHaveText('en')
    await contextA.close()

    // Device B: independent context, no shared storage — Turkish until sign-in,
    // then the stored account preference wins.
    const contextB = await browser.newContext()
    const pageB = await contextB.newPage()
    tagWithClientIp(pageB)
    await pageB.goto('/')
    await expect(pageB.locator('html')).toHaveAttribute('lang', 'tr')
    await signInWithFixture(pageB, VERIFIED_USER)
    await expect(pageB.locator('html')).toHaveAttribute('lang', 'en')

    // Restore the seeded account preference to `tr` so sibling specs stay stable.
    await pageB.goto('/settings')
    await pageB
      .getByRole('group', { name: EN('accountSettings.languageLabel') })
      .getByRole('button', { name: EN('header.turkish') })
      .click()
    await expect(pageB.getByTestId('language-preference')).toHaveText('tr')
    await contextB.close()
  })
})

/* ------------------------------------------------------------------ *
 * TC-XC-006 — disclaimer sweep across the seven research surfaces
 * ------------------------------------------------------------------ */

const ANONYMOUS_RESEARCH_SURFACES = [
  '/',
  '/stocks',
  '/screener',
  '/stocks/ALFA',
  '/stocks/ALFA/dcf',
]

test.describe('TC-XC-006 — disclaimer sweep across the seven research surfaces', () => {
  test('the informational-only disclaimer is present on the five anonymous surfaces', async ({
    page,
  }) => {
    for (const path of ANONYMOUS_RESEARCH_SURFACES) {
      await page.goto(path)
      const disclaimer = page.locator('[data-slot="disclaimer"]').first()
      await expect(
        disclaimer,
        `disclaimer missing on ${path}`,
      ).toBeVisible()
      await expect(disclaimer).toContainText(TR('disclaimer.text'))
    }
  })

  test('the disclaimer is present on the two signed-in research surfaces', async ({
    page,
  }) => {
    tagWithClientIp(page)
    await signInWithFixture(page, VERIFIED_USER)
    await switchLanguage(page, 'tr')

    for (const path of ['/screens', '/scenarios']) {
      await page.goto(path)
      const disclaimer = page.locator('[data-slot="disclaimer"]').first()
      await expect(
        disclaimer,
        `disclaimer missing on ${path}`,
      ).toBeVisible()
      await expect(disclaimer).toContainText(TR('disclaimer.text'))
    }
  })
})

/* ------------------------------------------------------------------ *
 * TC-XC-021 — no real-time framing on market-data surfaces
 * ------------------------------------------------------------------ */

/** Live/countdown framing terms that must never appear on an EOD surface. */
const LIVE_FRAMING = /canlı|gerçek zamanlı|real-?time|live|countdown|geri sayım/i

async function assertNoLiveFraming(page: Page): Promise<void> {
  const matches = await page.evaluate((source) => {
    const re = new RegExp(source, 'i')
    const hits: string[] = []
    for (const el of Array.from(document.querySelectorAll('body *'))) {
      if (el.children.length > 0) continue
      const text = el.textContent?.trim() ?? ''
      if (text && re.test(text)) hits.push(text)
    }
    return hits
  }, LIVE_FRAMING.source)
  expect(matches, `live/countdown framing found: ${matches.join(' | ')}`).toEqual([])
}

test.describe('TC-XC-021 — no real-time framing on market-data surfaces', () => {
  test('SCR-003 is quiet and EOD-framed after a run', async ({ page }) => {
    await page.goto('/screener')
    await page.getByTestId('metric-select').selectOption('pe')
    await page.getByTestId('add-criterion').click()
    const row = page.getByTestId('criterion-row').last()
    await row.getByTestId('criterion-bound').selectOption('max')
    await row.getByTestId('criterion-max').fill('15')
    await page.getByTestId('run-button').click()
    await expect(page.getByTestId('results')).toBeVisible()

    await assertNetworkQuiet(page, {
      windowMs: 3_000,
      match: /\/api\/v1\/(?!auth\/session)/,
    })
    await expect(page.getByTestId('results-asof')).toBeVisible()
    await assertNoLiveFraming(page)
  })

  test('SCR-005 is quiet and EOD-framed', async ({ page }) => {
    await page.goto('/stocks/ALFA', { waitUntil: 'networkidle' })
    await assertNetworkQuiet(page, {
      windowMs: 3_000,
      match: /\/api\/v1\/(?!auth\/session)/,
    })
    await assertNoLiveFraming(page)
  })

  test('SCR-006 is quiet and EOD-framed', async ({ page }) => {
    await page.goto('/stocks/ALFA/dcf', { waitUntil: 'networkidle' })
    await expect(page.getByTestId('dcf-price-asof')).toBeVisible()
    await assertNetworkQuiet(page, {
      windowMs: 3_000,
      match: /\/api\/v1\/(?!auth\/session)/,
    })
    await assertNoLiveFraming(page)
  })
})
