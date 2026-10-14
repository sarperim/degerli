import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

import { API_BASE_URL, MAILPIT_BASE_URL } from './config'
import { catalogValue, loadCatalog } from './helpers/catalog'
import { MailpitClient } from './helpers/mailpit'

/**
 * TC-ACC-030 (L4) — the SCR-009 password-reset flow against the seeded
 * compose.ci stack (`.pipeline/testing/user-accounts.md`).
 *
 * Covers the request stage → enumeration-neutral confirmation, the set-password
 * stage reached directly from the e-mailed link (external entry point), inline
 * policy validation, the expired/invalid-link explanation with its re-request
 * path, and success → signed-in state with saved work intact.
 *
 * Copy is read from the checked-in catalogs (§8.3) so assertions never hardcode
 * prose. The `/auth/*` 5/min budget is keyed by client IP, and every full page
 * load re-issues `GET /auth/session` + `GET /auth/csrf-token` — both of which
 * share that bucket — so a test that navigates more than once would spend more
 * than the 5/min budget under one key and trip `RATE_LIMITED`. As
 * `sign-in.spec.ts`/`account-settings.spec.ts` do, each request is tagged with its
 * own `X-Forwarded-For` (I-XC-2: the limiter keys on a test-controllable IP
 * outside production); the product's `03` §12 limit is itself unchanged.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

// The browser's requests all share the SPA proxy's address, so tag each API call
// with its own client IP so no single `/auth/*` bucket is exhausted by the flow.
let ipCounter = 0
function tagWithClientIp(page: Page): void {
  void page.route('**/api/v1/**', (route) => {
    ipCounter += 1
    return route.continue({
      headers: {
        ...route.request().headers(),
        'X-Forwarded-For': `10.10.1.${(ipCounter % 250) + 1}`,
      },
    })
  })
}

/** Seeded verified user A (fixtures) — has a saved screen, so "saved work intact" is observable. */
const USER_A = 'user-a@degerli.test'
const SAVED_SCREEN_NAME = 'Ekranım'
const NEW_PASSWORD = 'new-correct-horse-battery'
/** User A's seeded password (`fixtures/Degerli.Fixtures/FixtureUniverse.cs`), restored after the flow. */
const USER_A_ORIGINAL_PASSWORD = 'FixturePass1!'

/**
 * Restores the shared seeded user A password to its original value through the
 * public reset flow, so spec files that run after this one against the same
 * un-reseeded `compose.ci` stack (e.g. `sign-in.spec.ts` TC-ACC-031) still
 * authenticate as user A.
 *
 * `compose.ci.yml` seeds the fixture universe once and Playwright runs with no
 * per-file reseed, so this file must not leave shared fixture state mutated. The
 * e-mailed token is single-use, so the mailbox is cleared and a fresh link is
 * requested; each call carries its own `X-Forwarded-For` so it never shares an
 * `/auth/*` rate-limit bucket with the flow under test.
 */
async function restoreUserAPassword(request: APIRequestContext): Promise<void> {
  const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)
  await mailpit.clear()

  const forgot = await request.post(`${API_BASE_URL}/api/v1/auth/forgot-password`, {
    headers: { 'X-Forwarded-For': '10.10.2.1' },
    data: { email: USER_A },
  })
  expect(forgot.status()).toBe(200)

  const { link } = await mailpit.waitForLink(/\/reset-password\?token=/, {
    description: 'the password-reset link used to restore user A',
  })
  const token = new URL(link).searchParams.get('token')

  const reset = await request.post(`${API_BASE_URL}/api/v1/auth/reset-password`, {
    headers: { 'X-Forwarded-For': '10.10.2.2' },
    data: { token, newPassword: USER_A_ORIGINAL_PASSWORD },
  })
  expect(reset.status()).toBe(200)
}

test.describe('TC-ACC-030 request stage — neutral confirmation', () => {
  test('shows an identical neutral confirmation for a registered and an unknown address', async ({
    page,
  }) => {
    tagWithClientIp(page)

    const confirmOnce = async (email: string) => {
      await page.goto('/reset-password')
      await page.getByLabel(TR('passwordReset.emailLabel')).fill(email)
      await page.getByRole('button', { name: TR('passwordReset.requestSubmit') }).click()
      await expect(page.getByText(TR('passwordReset.neutralTitle'))).toBeVisible()
      await expect(page.getByText(TR('passwordReset.neutralBody'))).toBeVisible()
    }

    await confirmOnce(USER_A)
    // No account-enumeration signal: the confirmation never echoes the address.
    await expect(page.getByText(USER_A)).toHaveCount(0)

    await confirmOnce('nobody@degerli.test')
  })
})

test.describe('TC-ACC-030 expired/invalid link', () => {
  test('shows a plain-language explanation and a re-request path', async ({ page }) => {
    tagWithClientIp(page)
    await page.goto('/reset-password?token=expired-or-invalid')
    await page
      .getByLabel(TR('passwordReset.passwordLabel'), { exact: false })
      .fill(NEW_PASSWORD)
    await page.getByRole('button', { name: TR('passwordReset.setSubmit') }).click()

    await expect(page.getByText(TR('passwordReset.expiredTitle'))).toBeVisible()
    await expect(page.getByText(TR('passwordReset.expiredBody'))).toBeVisible()

    const reRequest = page.getByRole('link', {
      name: TR('passwordReset.requestNewLink'),
    })
    await expect(reRequest).toHaveAttribute('href', '/reset-password')

    // The re-request path returns the user to the request stage.
    await reRequest.click()
    await expect(page.getByLabel(TR('passwordReset.emailLabel'))).toBeVisible()
  })
})

test.describe('TC-ACC-030 full reset via the e-mailed link', () => {
  test('the e-mailed link sets a new password and signs the user in with saved work intact', async ({
    page,
    request,
  }) => {
    tagWithClientIp(page)
    const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)

    // This flow mutates the shared seeded user A password; restore it on the way
    // out — even when an assertion fails — so later spec files that run against
    // the same un-reseeded stack still authenticate as user A.
    let passwordChanged = false
    try {
      await mailpit.clear()

      // 1. Request the reset; a neutral confirmation is shown regardless.
      await page.goto('/reset-password')
      await page.getByLabel(TR('passwordReset.emailLabel')).fill(USER_A)
      await page.getByRole('button', { name: TR('passwordReset.requestSubmit') }).click()
      await expect(page.getByText(TR('passwordReset.neutralTitle'))).toBeVisible()

      // 2. The link from the e-mailed message opens the set-password stage directly
      //    from an e-mail context (external entry point).
      const { link } = await mailpit.waitForLink(/\/reset-password\?token=/, {
        description: 'the password-reset link',
      })
      await page.goto(link)
      await expect(
        page.getByLabel(TR('passwordReset.passwordLabel'), { exact: false }),
      ).toBeVisible()

      // 3. Set a valid new password with the inline rule satisfied.
      await page
        .getByLabel(TR('passwordReset.passwordLabel'), { exact: false })
        .fill(NEW_PASSWORD)
      await page.getByRole('button', { name: TR('passwordReset.setSubmit') }).click()
      passwordChanged = true

      // 4. Success → placed into a signed-in state (UXR-ACC-015).
      await expect(page.getByText(TR('passwordReset.successTitle'))).toBeVisible()

      // 5. Saved work is intact: the still-signed-in session lists user A's screen.
      const screens = await page.request.get(`${API_BASE_URL}/api/v1/me/screens`)
      expect(screens.status()).toBe(200)
      const body = (await screens.json()) as { screens: Array<{ name: string }> }
      expect(body.screens.map((screen) => screen.name)).toContain(SAVED_SCREEN_NAME)
    } finally {
      if (passwordChanged) {
        await restoreUserAPassword(request)
      }
    }
  })
})
