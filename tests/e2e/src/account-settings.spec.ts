import { randomUUID } from 'node:crypto'

import { expect, test, type Page } from '@playwright/test'

import { MAILPIT_BASE_URL } from './config'
import { catalogValue, loadCatalog } from './helpers/catalog'
import { extractLinks, MailpitClient } from './helpers/mailpit'

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
 * Every account is created in-test (register → verify via MailPit) so the specs
 * never read or mutate another test's state (FU §8.3). Copy is read from the
 * checked-in catalogs, never hardcoded (§5.4/§8.3).
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

const PASSWORD = 'FixturePass1!'

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

async function registerAccount(page: Page, email: string): Promise<void> {
  await page.goto('/register')
  await page.getByLabel(TR('register.emailLabel'), { exact: false }).fill(email)
  await page
    .getByLabel(TR('register.passwordLabel'), { exact: false })
    .fill(PASSWORD)
  await page
    .getByLabel(TR('register.consentLabel'), { exact: false })
    .check()
  await page
    .getByRole('form', { name: TR('register.formAriaLabel') })
    .getByRole('button', { name: TR('register.submit') })
    .click()
  await expect(page.getByTestId('registration-verification-notice')).toBeVisible()
}

async function signIn(page: Page, email: string, password = PASSWORD): Promise<void> {
  await page.goto('/sign-in')
  await page.getByLabel(TR('signIn.emailLabel'), { exact: false }).fill(email)
  await page.getByLabel(TR('signIn.passwordLabel'), { exact: false }).fill(password)
  await page
    .getByRole('form', { name: TR('signIn.formAriaLabel') })
    .getByRole('button', { name: TR('signIn.submit') })
    .click()
}

/** Poll MailPit (bounded) for the verification link addressed to `email`. */
async function awaitVerificationLink(
  mailpit: MailpitClient,
  email: string,
): Promise<string> {
  const summary = await mailpit.waitForMessage(
    (candidate) => candidate.To.some((to) => to.Address === email),
    { description: `a verification e-mail for ${email}` },
  )
  const message = await mailpit.get(summary.ID)
  const link = extractLinks(message).find((url) =>
    /\/verify-email\?token=/.test(url),
  )
  expect(link, `a verification link in the e-mail to ${email}`).toBeTruthy()
  return link as string
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
    request,
  }) => {
    const email = uniqueEmail('acc032')
    const screenName = `Silinecek ${randomUUID().slice(0, 8)}`

    const contextA = await browser.newContext()
    const pageA = await contextA.newPage()
    tagWithClientIp(pageA)

    // Register → verify (link read from MailPit) → save a screen.
    const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)
    await mailpit.clear()
    await registerAccount(pageA, email)
    const verificationLink = await awaitVerificationLink(mailpit, email)
    await pageA.goto(verificationLink)
    await expect(pageA.getByText(TR('verifyEmail.verifiedTitle'))).toBeVisible()

    await saveScreen(pageA, screenName)
    // The screen is reachable while signed in (so its later loss is meaningful).
    await pageA.goto('/screens')
    await expect(pageA.getByTestId('saved-screens-list')).toContainText(screenName)

    // Delete the account through the double confirmation (UXR-ACC-019).
    await pageA.goto('/settings')
    await pageA.getByTestId('delete-account').click()
    const dialog = pageA.getByRole('dialog')
    await expect(dialog).toContainText(
      TR('accountSettings.deleteDialogDescription'),
    )
    await dialog
      .getByRole('button', { name: TR('accountSettings.deleteConfirm') })
      .click()

    // Removed and signed out: back on the anonymous home, header shows guest.
    await expect(pageA).toHaveURL(/\/$/)
    await expect(pageA.getByText(TR('header.anonymous'))).toBeVisible()

    // Previously saved screens are no longer accessible — the signed-out visit
    // to My Saved Screens presents the sign-in path, no rows.
    await pageA.goto('/screens')
    await expect(pageA.getByTestId('saved-screens-anonymous')).toBeVisible()
    await expect(pageA.getByTestId('saved-screens-list')).toHaveCount(0)

    // The account is gone: the old credentials no longer sign in.
    await signIn(pageA, email)
    await expect(pageA.getByRole('alert')).toContainText(
      TR('signIn.invalidCredentials'),
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
