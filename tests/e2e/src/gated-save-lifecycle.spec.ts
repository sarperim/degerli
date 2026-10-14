import { randomUUID } from 'node:crypto'

import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'
import {
  buildScreenerSaveDraft,
  fillRegister,
  fillSignIn,
  mailpitFor,
  registerAccount,
  tagWithClientIp,
  uniqueEmail,
  waitForVerificationLink,
} from './helpers/accounts'

/**
 * Gated-save lifecycle flows (TKT-int-005) — the cross-domain end-to-end specs
 * that tie SCR + VAL + ACC together around the login-to-save pattern with
 * e-mail-verification gating.
 *
 * TCs (`.pipeline/testing/stock-screening.md`, `valuation-dcf.md`,
 * `user-accounts.md` §2 Group E/G):
 *
 * - TC-SCR-028 — anonymous save → register → verify → return → save (screener).
 * - TC-SCR-032 — unverified gate unblocks on in-session verification (screener).
 * - TC-VAL-024 — anonymous save → register → verify → save (DCF calculator).
 * - TC-ACC-027 — verification applies to an open session from another tab.
 * - TC-ACC-028 — sign-in from a save prompt returns to work; preference applied.
 *
 * Copy is read from the checked-in catalogs (§8.3), never hardcoded. Every flow
 * provisions its own throwaway account through the real register → verify loop
 * (see `helpers/accounts.ts` on why shared seeded fixtures are avoided), and
 * reads the verification link via MailPit with bounded polling.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

/** ALFA is a fully covered stock (FU); its baseline is seeded at the fixture anchor. */
const DCF_PATH = '/stocks/ALFA/dcf'

/** A distinct, valid discount rate so "assumptions intact" is observable. */
const DISCOUNT_RATE = '17'

test.describe('TC-SCR-028 — anonymous save → register → verify → return → save', () => {
  test('the full gated-save lifecycle completes on the screener', async ({ page, request }) => {
    tagWithClientIp(page)
    const mailpit = mailpitFor(request)
    await mailpit.clear()

    const email = uniqueEmail('scr028')
    const screenName = `Kaydedilen ${randomUUID().slice(0, 8)}`

    // 1. Anonymous draft, save attempt → sign-in/register prompt.
    await page.goto('/screener')
    await buildScreenerSaveDraft(page, screenName)
    await page.getByTestId('save-button').click()
    await expect(page.getByTestId('save-anonymous')).toBeVisible()

    // 2. Register from the prompt (verification e-mail is dispatched).
    await page
      .getByTestId('save-anonymous')
      .getByRole('link', { name: TR('screener.register') })
      .click()
    await expect(page).toHaveURL(/\/register/)
    await fillRegister(page, email)

    // 3. Read the verification link from MailPit.
    const link = await waitForVerificationLink(request, email)

    // 4. Return to the preserved work: criteria + name intact.
    await page.getByRole('link', { name: TR('register.returnToWork') }).click()
    await expect(page).toHaveURL(/\/screener$/)
    await expect(page.getByTestId('criterion-row')).toHaveCount(1)
    await expect(page.getByTestId('save-name')).toHaveValue(screenName)

    // 5. Save is blocked with the verify-your-e-mail message (unverified account).
    await page.getByTestId('save-button').click()
    await expect(page.getByTestId('save-unverified')).toBeVisible()

    // 6. Verify via the e-mailed link in the same session.
    await page.goto(link)
    await expect(page.getByText(TR('verifyEmail.verifiedTitle'))).toBeVisible()

    // 7. The save now completes without re-authentication; drafts survived.
    await page.goto('/screener')
    await expect(page.getByTestId('criterion-row')).toHaveCount(1)
    await expect(page.getByTestId('save-name')).toHaveValue(screenName)
    await page.getByTestId('save-button').click()
    await expect(page.getByTestId('save-confirmation')).toContainText(screenName)

    // 8. The screen appears in My Saved Screens without a manual refresh.
    await page.goto('/screens')
    await expect(page.getByTestId('saved-screens-list')).toContainText(screenName)
  })
})

test.describe('TC-SCR-032 — unverified gate unblocks on in-session verification', () => {
  test("a signed-in unverified user's save is blocked until verifying in another tab", async ({
    browser,
    request,
  }) => {
    const context = await browser.newContext()
    const page = await context.newPage()
    tagWithClientIp(page)
    const mailpit = mailpitFor(request)
    await mailpit.clear()

    const email = uniqueEmail('scr032')
    const screenName = `Tekrar ${randomUUID().slice(0, 8)}`

    // Register (signed in, unverified) and read the verification link.
    await registerAccount(page, email, { returnUrl: '/screener' })
    const link = await waitForVerificationLink(request, email)

    // Return to the screener and attempt to save → blocked.
    await page.getByRole('link', { name: TR('register.returnToWork') }).click()
    await expect(page).toHaveURL(/\/screener$/)
    await buildScreenerSaveDraft(page, screenName)
    await page.getByTestId('save-button').click()
    await expect(page.getByTestId('save-unverified')).toBeVisible()

    // Verify in a second tab of the same session.
    const secondTab = await context.newPage()
    tagWithClientIp(secondTab)
    await secondTab.goto(link)
    await expect(secondTab.getByText(TR('verifyEmail.verifiedTitle'))).toBeVisible()

    // Back on the open session, the next save attempt succeeds — no re-auth.
    await page.bringToFront()
    await page.getByTestId('save-button').click()
    await expect(page.getByTestId('save-confirmation')).toContainText(screenName)

    await context.close()
  })
})

test.describe('TC-ACC-027 — verification applies to an open session from another tab', () => {
  test('the open session refetches the verified state and can then save', async ({
    browser,
    request,
  }) => {
    const context = await browser.newContext()
    const page = await context.newPage()
    tagWithClientIp(page)
    const mailpit = mailpitFor(request)
    await mailpit.clear()

    const email = uniqueEmail('acc027')
    const screenName = `Refetch ${randomUUID().slice(0, 8)}`

    await registerAccount(page, email, { returnUrl: '/screener' })
    const link = await waitForVerificationLink(request, email)

    await page.getByRole('link', { name: TR('register.returnToWork') }).click()
    await buildScreenerSaveDraft(page, screenName)
    await page.getByTestId('save-button').click()
    await expect(page.getByTestId('save-unverified')).toBeVisible()

    // Verify in a second tab of the same browser session.
    const secondTab = await context.newPage()
    tagWithClientIp(secondTab)
    await secondTab.goto(link)
    await expect(secondTab.getByText(TR('verifyEmail.verifiedTitle'))).toBeVisible()

    // Actually switch focus to the second tab, then back to the first, so returning
    // raises the window-focus transition the session query listens for. On return,
    // the query refetches (M-9, refetchOnWindowFocus) and the blocked save becomes
    // completable without re-authentication.
    await secondTab.bringToFront()
    const sessionRefetch = page.waitForRequest(
      (candidate) =>
        candidate.method() === 'GET' && candidate.url().includes('/api/v1/auth/session'),
      { timeout: 10_000 },
    )
    await page.bringToFront()
    // Headless Chromium does not raise a real tab-focus/visibility transition on
    // `bringToFront`, so raise the same browser events the session query subscribes
    // to (`refetchOnWindowFocus`, M-9) to deterministically model returning to the tab.
    await page.evaluate(() => {
      window.dispatchEvent(new Event('visibilitychange'))
      window.dispatchEvent(new Event('focus'))
    })
    await sessionRefetch

    await page.getByTestId('save-button').click()
    await expect(page.getByTestId('save-confirmation')).toContainText(screenName)

    await context.close()
  })
})

test.describe('TC-VAL-024 — anonymous save → register → verify → save (DCF)', () => {
  test('the gated-save lifecycle completes on the calculator; the scenario is listed', async ({
    page,
    request,
  }) => {
    tagWithClientIp(page)
    const mailpit = mailpitFor(request)
    await mailpit.clear()

    const email = uniqueEmail('val024')
    const scenarioName = `Senaryo ${randomUUID().slice(0, 8)}`

    // 1. Anonymous assumptions + save attempt → sign-in/register prompt.
    await page.goto(DCF_PATH)
    await expect(page.getByTestId('dcf-params')).toBeVisible()
    await page.getByTestId('dcf-param-discount_rate').fill(DISCOUNT_RATE)
    await page.getByTestId('dcf-scenario-name').fill(scenarioName)
    await page.getByTestId('dcf-scenario-save').click()
    await expect(page.getByTestId('dcf-scenario-anonymous')).toBeVisible()

    // 2. Register from the prompt.
    await page
      .getByTestId('dcf-scenario-anonymous')
      .getByRole('link', { name: TR('dcf.scenario.register') })
      .click()
    await expect(page).toHaveURL(/\/register/)
    await fillRegister(page, email)
    const link = await waitForVerificationLink(request, email)

    // 3. Return to work: assumptions + name intact.
    await page.getByRole('link', { name: TR('register.returnToWork') }).click()
    await expect(page).toHaveURL(/\/stocks\/ALFA\/dcf$/)
    await expect(page.getByTestId('dcf-param-discount_rate')).toHaveValue(DISCOUNT_RATE)
    await expect(page.getByTestId('dcf-scenario-name')).toHaveValue(scenarioName)

    // 4. Unverified gate.
    await page.getByTestId('dcf-scenario-save').click()
    await expect(page.getByTestId('dcf-scenario-unverified')).toBeVisible()

    // 5. Verify in-session, then the save completes without re-authentication.
    await page.goto(link)
    await expect(page.getByText(TR('verifyEmail.verifiedTitle'))).toBeVisible()

    await page.goto(DCF_PATH)
    await expect(page.getByTestId('dcf-param-discount_rate')).toHaveValue(DISCOUNT_RATE)
    await page.getByTestId('dcf-scenario-save').click()
    await expect(page.getByTestId('dcf-scenario-saved')).toContainText(scenarioName)

    // 6. The scenario appears in the calculator's picker without a manual refresh…
    await expect(page.getByTestId('dcf-scenario-picker')).toContainText(scenarioName)

    // …and in My DCF Scenarios (SCR-011).
    await page.goto('/scenarios')
    await expect(page.getByTestId('dcf-scenarios-list')).toContainText(scenarioName)
  })
})

test.describe('TC-ACC-028 — sign-in from a save prompt returns to work; preference applied', () => {
  test('signing in from the DCF save prompt restores the work and applies English', async ({
    browser,
    request,
  }) => {
    const email = uniqueEmail('acc028')

    // Provision a verified account whose stored preference is English. The seeded
    // EN account (`user-c`) is deleted by TC-ACC-032 against this same shared,
    // un-reseeded stack, so the equivalent account is created through the real
    // register → verify loop to keep this spec order-independent
    // (`.pipeline/testing/user-accounts.md` TC-ACC-028; see `helpers/accounts.ts`).
    const setupContext = await browser.newContext()
    const setupPage = await setupContext.newPage()
    tagWithClientIp(setupPage)
    const mailpit = mailpitFor(request)
    await mailpit.clear()

    await registerAccount(setupPage, email)
    const link = await waitForVerificationLink(request, email)
    await setupPage.goto(link)
    await expect(setupPage.getByText(TR('verifyEmail.verifiedTitle'))).toBeVisible()

    await setupPage.goto('/settings')
    const languageGroup = setupPage.getByRole('group', {
      name: TR('accountSettings.languageLabel'),
    })
    await languageGroup.getByRole('button', { name: TR('header.english') }).click()
    await expect(setupPage.getByTestId('language-preference')).toHaveText('en')
    await setupContext.close()

    // Anonymous DCF assumptions + save attempt → prompt.
    const context = await browser.newContext()
    const page = await context.newPage()
    tagWithClientIp(page)
    const scenarioName = `Geri donus ${randomUUID().slice(0, 8)}`

    await page.goto(DCF_PATH)
    await expect(page.getByTestId('dcf-params')).toBeVisible()
    await page.getByTestId('dcf-param-discount_rate').fill(DISCOUNT_RATE)
    await page.getByTestId('dcf-scenario-name').fill(scenarioName)
    await page.getByTestId('dcf-scenario-save').click()
    await expect(page.getByTestId('dcf-scenario-anonymous')).toBeVisible()

    // Sign in from the prompt.
    await page
      .getByTestId('dcf-scenario-anonymous')
      .getByRole('link', { name: TR('dcf.scenario.signIn') })
      .click()
    await expect(page).toHaveURL(/\/sign-in/)
    await fillSignIn(page, email)

    // Returned to the calculator with assumptions intact, in English (preference).
    await expect(page).toHaveURL(/\/stocks\/ALFA\/dcf$/)
    await expect(page.locator('html')).toHaveAttribute('lang', 'en')
    await expect(page.getByTestId('dcf-param-discount_rate')).toHaveValue(DISCOUNT_RATE)
    await expect(page.getByTestId('dcf-scenario-name')).toHaveValue(scenarioName)

    // The save completes (verified, signed-in account).
    await page.getByTestId('dcf-scenario-save').click()
    await expect(page.getByTestId('dcf-scenario-saved')).toContainText(scenarioName)

    await context.close()
  })
})
