import { randomUUID } from 'node:crypto'

import { expect, test, type Page } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * SCR-010 account-settings L4 flows (`.pipeline/testing/user-accounts.md` §2
 * Group G; test strategy §5.4 — Playwright against the full compose.ci stack).
 *
 * TC-ACC-029 (L4) — language preference across devices: setting English in the
 * settings screen of browser context A applies immediately; signing in on an
 * independent browser context B applies the stored account preference.
 *
 * TC-ACC-032 (L4) — delete account end-to-end: the confirmation warns about
 * losing saved screens and scenarios; on confirm the account is removed, the
 * user is signed out, previously saved screens are no longer accessible, and
 * other users' data is unaffected.
 *
 * Copy is read from the checked-in catalogs, never hardcoded (§5.4/§8.3). Form
 * fields are addressed by their stable DOM ids so a helper works in either UI
 * language.
 *
 * NOTE (escalated): TC-ACC-032 drives a seeded **verified** account (`user-c`,
 * FU §9). The test ideal is to register → verify a throwaway account in-test, but
 * the API ships only `LoggingMailDispatcher` (no SMTP adapter), so the compose
 * stack never delivers a verification link to MailPit. `user-c` is verified and
 * unused by any other e2e spec, so the deletion tests the same contract without
 * colliding with a sibling test's account. See the fixer report for the upstream
 * gap (test strategy §7 "MailPit, links read via its API").
 */

const tr = loadCatalog('tr')
const en = loadCatalog('en')
const TR = (key: string) => catalogValue(tr, key)
const EN = (key: string) => catalogValue(en, key)

/** The fixture password for every seeded account (FU §9). */
const PASSWORD = 'FixturePass1!'

/**
 * A seeded verified account (preference `en`, FU §9) that this spec deletes; no
 * other e2e spec references it.
 */
const DELETE_USER_EMAIL = 'user-c@degerli.test'
/** A seeded verified account with saved content (FU §9) used as "another user". */
const OTHER_USER_EMAIL = 'user-a@degerli.test'
const OTHER_USER_SCREEN = 'Ekranım'

// The API's /auth/* rate-limit budget is 5/min keyed by client IP. The browser's
// requests all share the SPA proxy's address, so — exactly as the L2 integration
// tests do and as `sign-in.spec.ts` does — each request is tagged with its own
// X-Forwarded-For so an unrelated flow is never throttled during this spec.
let ipCounter = 0
function tagWithClientIp(page: Page): void {
  void page.route('**/api/v1/**', (route) => {
    ipCounter += 1
    return route.continue({
      headers: {
        ...route.request().headers(),
        'X-Forwarded-For': `198.51.100.${(ipCounter % 250) + 1}`,
      },
    })
  })
}

function uniqueEmail(prefix: string): string {
  return `${prefix}-${randomUUID()}@example.test`
}

/**
 * Fill and submit a form by its stable DOM ids, so the helper is independent of
 * the active UI language (the accessible label text is localized).
 */
async function submitForm(
  page: Page,
  ids: { email: string; password: string; consent?: string },
  email: string,
  password: string,
): Promise<void> {
  await page.locator(`#${ids.email}`).fill(email)
  await page.locator(`#${ids.password}`).fill(password)
  if (ids.consent) await page.locator(`#${ids.consent}`).check()
  await page.locator('form button[type="submit"]').click()
}

async function registerAccount(page: Page, email: string): Promise<void> {
  await page.goto('/register')
  await submitForm(
    page,
    { email: 'register-email', password: 'register-password', consent: 'register-consent' },
    email,
    PASSWORD,
  )
  await expect(page.getByTestId('registration-verification-notice')).toBeVisible()
}

async function signIn(page: Page, email: string, password = PASSWORD): Promise<void> {
  await page.goto('/sign-in')
  await submitForm(
    page,
    { email: 'sign-in-email', password: 'sign-in-password' },
    email,
    password,
  )
}

/** Build and persist a saved screen through the real screener UI. */
async function saveScreen(page: Page, name: string): Promise<void> {
  await page.goto('/screener')
  await expect(page.getByTestId('metric-select')).toBeVisible()
  await page.getByTestId('metric-select').selectOption('pe')
  await page.getByTestId('add-criterion').click()
  const row = page.getByTestId('criterion-row').last()
  await row.getByTestId('criterion-bound').selectOption('max')
  await row.getByTestId('criterion-max').fill('15')
  await page.getByTestId('save-name').fill(name)
  await page.getByTestId('save-button').click()
  await expect(page.getByTestId('save-confirmation')).toContainText(name)
}

test.describe('TC-ACC-029 — language preference across devices (L4)', () => {
  test('setting English applies immediately and an independent context applies it at sign-in', async ({
    browser,
  }) => {
    const email = uniqueEmail('acc029')

    // Context A: register (signed in), set English in the settings screen.
    const contextA = await browser.newContext()
    const pageA = await contextA.newPage()
    tagWithClientIp(pageA)

    await registerAccount(pageA, email)
    await pageA.goto('/settings')

    const languageGroup = pageA.getByRole('group', {
      name: TR('accountSettings.languageLabel'),
    })
    await expect(languageGroup).toBeVisible()
    await languageGroup
      .getByRole('button', { name: TR('header.english') })
      .click()

    // Immediate UI switch, with no reload (UXR-ACC-017).
    await expect(pageA.locator('html')).toHaveAttribute('lang', 'en')
    // And the account read reflects the persisted preference (PATCH /me).
    await expect(pageA.getByTestId('language-preference')).toHaveText('en')

    await contextA.close()

    // Context B: an independent browser context (no shared cookies/storage).
    const contextB = await browser.newContext()
    const pageB = await contextB.newPage()
    tagWithClientIp(pageB)

    // Default context language is Turkish; the account preference must win.
    await pageB.goto('/')
    await expect(pageB.locator('html')).toHaveAttribute('lang', 'tr')
    await signIn(pageB, email)
    await expect(pageB.locator('html')).toHaveAttribute('lang', 'en')

    await contextB.close()
  })
})

test.describe('TC-ACC-032 — delete account end-to-end (L4)', () => {
  test('confirm removes the account, signs the user out, and leaves others unaffected', async ({
    browser,
  }) => {
    const screenName = `Silinecek ${randomUUID().slice(0, 8)}`

    // A seeded verified account (preference `en`, FU §9) — see the file header
    // note on why the throwaway register→verify flow cannot run against MailPit.
    const contextA = await browser.newContext()
    const pageA = await contextA.newPage()
    tagWithClientIp(pageA)

    await signIn(pageA, DELETE_USER_EMAIL)
    // Sign-in applies the account preference (`en`); hold it for stable copy.
    await expect(pageA.locator('html')).toHaveAttribute('lang', 'en')

    // Create the account's own saved screen, then confirm it is reachable.
    await saveScreen(pageA, screenName)
    await pageA.goto('/screens')
    await expect(pageA.getByTestId('saved-screens-list')).toContainText(screenName)

    // Delete the account through the double confirmation (UXR-ACC-019).
    await pageA.goto('/settings')
    await pageA.getByTestId('delete-account').click()
    const dialog = pageA.getByRole('dialog')
    await expect(dialog).toContainText(EN('accountSettings.deleteDialogDescription'))
    await dialog
      .getByRole('button', { name: EN('accountSettings.deleteConfirm') })
      .click()

    // Removed and signed out: back on the anonymous home, header shows guest.
    await expect(pageA).toHaveURL(/\/$/)
    await expect(pageA.getByText(EN('header.anonymous'))).toBeVisible()

    // Previously saved screens are no longer accessible — the signed-out visit
    // to My Saved Screens presents the sign-in path, no rows.
    await pageA.goto('/screens')
    await expect(pageA.getByTestId('saved-screens-anonymous')).toBeVisible()
    await expect(pageA.getByTestId('saved-screens-list')).toHaveCount(0)

    // The account is gone: the old credentials no longer sign in.
    await signIn(pageA, DELETE_USER_EMAIL)
    await expect(pageA.getByRole('alert')).toContainText(
      EN('signIn.invalidCredentials'),
    )

    await contextA.close()

    // Another user's data is unaffected: the seeded account's saved screen is
    // still listed after the deletion above.
    const contextB = await browser.newContext()
    const pageB = await contextB.newPage()
    tagWithClientIp(pageB)
    await signIn(pageB, OTHER_USER_EMAIL)
    await expect(
      pageB.getByRole('button', { name: TR('header.signOut') }),
    ).toBeVisible()
    await pageB.goto('/screens')
    await expect(pageB.getByTestId('saved-screens-list')).toContainText(
      OTHER_USER_SCREEN,
    )

    await contextB.close()
  })
})
