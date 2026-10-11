import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * TC-ACC-031 (L4) — Sign out anywhere; language retained device-locally
 * (`.pipeline/testing/user-accounts.md` §2 Group G; UXR-ACC-010, SCR-008 flow 3).
 *
 * Against the seeded compose.ci stack: sign in as the verified fixture account
 * (FU §9), choose a device language, move to another screen, then sign out from
 * the header. The session ends, the header reflects the anonymous state, and the
 * chosen language stays active on the device — including across a reload.
 *
 * Copy is read from the checked-in catalogs (§8.3) so assertions never hardcode
 * prose.
 */

const tr = loadCatalog('tr')
const en = loadCatalog('en')
const TR = (key: string) => catalogValue(tr, key)
const EN = (key: string) => catalogValue(en, key)

/** A verified fixture account (`fixtures/Degerli.Fixtures/FixtureUniverse.cs`). */
const FIXTURE_EMAIL = 'user-a@degerli.test'
const FIXTURE_PASSWORD = 'FixturePass1!'

// The API's /auth/* rate-limit budget is 5/min keyed by client IP. The browser's
// requests all share the SPA proxy's address, so — exactly as the L2 integration
// tests do (`Acc003…NextClientIp`) — each request is tagged with its own
// X-Forwarded-For so an unrelated flow is never throttled during this spec.
let ipCounter = 0
function nextForwardedFor(): string {
  ipCounter += 1
  return `203.0.113.${ipCounter}`
}

test.describe('TC-ACC-031 — Sign out anywhere; language retained device-locally', () => {
  test('sign-out from the header ends the session and the language stays active', async ({
    page,
  }) => {
    await page.route('**/api/v1/**', (route) =>
      route.continue({
        headers: {
          ...route.request().headers(),
          'X-Forwarded-For': nextForwardedFor(),
        },
      }),
    )

    // Sign in as the fixture account.
    await page.goto('/sign-in')
    await page.getByLabel(TR('signIn.emailLabel')).fill(FIXTURE_EMAIL)
    await page.getByLabel(TR('signIn.passwordLabel')).fill(FIXTURE_PASSWORD)
    await page
      .getByRole('form', { name: TR('signIn.formAriaLabel') })
      .getByRole('button', { name: TR('signIn.submit') })
      .click()

    // Signed in: the header now offers the sign-out control.
    await expect(page.getByRole('button', { name: TR('header.signOut') })).toBeVisible()

    // Choose English on the device (device-local selection, UXR-G-014).
    await page.getByRole('button', { name: TR('header.english') }).click()
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')

    // Move to another screen; the header persists with the signed-in state.
    await page.getByRole('link', { name: EN('nav.stocks') }).click()
    await expect(page).toHaveURL(/\/stocks$/)
    await expect(page.getByRole('button', { name: EN('header.signOut') })).toBeVisible()

    // Sign out from the header.
    await page.getByRole('button', { name: EN('header.signOut') }).click()

    // The session ends and the header reflects the anonymous state.
    await expect(page.getByText(EN('header.anonymous'))).toBeVisible()
    await expect(page.getByRole('button', { name: EN('header.signOut') })).toHaveCount(0)

    // The language selection remains active on the device — even across a reload.
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')
    await page.reload()
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')
    await expect(page.getByText(EN('header.anonymous'))).toBeVisible()
  })
})
