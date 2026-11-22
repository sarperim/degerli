import { randomUUID } from 'node:crypto'

import { expect, test, type Page } from '@playwright/test'

import { MAILPIT_BASE_URL } from './config'
import { catalogValue, loadCatalog } from './helpers/catalog'
import { MailpitClient } from './helpers/mailpit'

/**
 * Single-IP end-to-end authentication & password-reset workflows (L4).
 *
 * Unlike the older per-request IP-rotating specs, each test here pins ONE
 * constant `X-Forwarded-For` IP for every `/api/v1/**` call across its browser
 * context(s) and asserts zero `429` responses — proving real single-IP browser
 * usage stays comfortably within `/auth/*` rate-limit budgets.
 */

const tr = loadCatalog('tr')
const en = loadCatalog('en')
const TR = (key: string) => catalogValue(tr, key)
const EN = (key: string) => catalogValue(en, key)

const INITIAL_PASSWORD = 'AuditPass123!'
const NEW_PASSWORD = 'NewAuditPass123!'

/**
 * Pins every `/api/v1/**` request on `page` to one constant client IP and
 * collects any `429` responses observed during the test.
 */
async function pinClientIp(page: Page, ip: string): Promise<string[]> {
  const rateLimitedUrls: string[] = []
  page.on('response', (response) => {
    if (response.status() === 429) {
      rateLimitedUrls.push(`${response.request().method()} ${response.url()}`)
    }
  })
  await page.route('**/api/v1/**', (route) =>
    route.continue({
      headers: {
        ...route.request().headers(),
        'X-Forwarded-For': ip,
      },
    }),
  )
  return rateLimitedUrls
}

function uniqueEmail(prefix: string): string {
  return `${prefix}-${randomUUID()}@example.test`
}

test.describe('Single-IP auth workflows (no 429s)', () => {
  test('register in EN → cross-context verify with sign-in return → sign out/in → delete account', async ({
    browser,
    request,
  }) => {
    const ip = '203.0.113.210'
    const email = uniqueEmail('single-ip-reg')
    const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)
    await mailpit.clear()

    const context1 = await browser.newContext()
    const page1 = await context1.newPage()
    const rateLimited1 = await pinClientIp(page1, ip)

    const context2 = await browser.newContext()
    const page2 = await context2.newPage()
    const rateLimited2 = await pinClientIp(page2, ip)

    try {
      // 1. Visit home, switch to English, and navigate to /register via SPA header button.
      await page1.goto('/')
      await page1
        .getByRole('banner')
        .getByRole('button', { name: TR('header.english'), exact: true })
        .click()
      await expect(page1.locator('html')).toHaveAttribute('lang', 'en')

      await page1.evaluate(() => {
        ;(window as unknown as { __spaMarker?: string }).__spaMarker = 'alive'
      })
      await page1
        .getByRole('banner')
        .getByRole('button', { name: EN('header.register'), exact: true })
        .click()
      await expect(page1).toHaveURL(/\/register$/)
      const spaMarker = await page1.evaluate(
        () => (window as unknown as { __spaMarker?: string }).__spaMarker,
      )
      expect(spaMarker).toBe('alive')

      // 2. Register a unique throwaway account in English.
      await page1.locator('#register-email').fill(email)
      await page1.locator('#register-password').fill(INITIAL_PASSWORD)
      await page1.locator('#register-consent').check()
      await page1.locator('form button[type="submit"]').click()
      await expect(page1.getByTestId('registration-verification-notice')).toBeVisible()

      // 3. Read the verification link from MailPit.
      const { link } = await mailpit.waitForLink(/\/verify-email\?token=/, {
        description: 'the e-mail verification link',
      })

      // 4. Open the verification link in a fresh unauthenticated context pinned to the same IP.
      await page2.goto(link)
      await expect(page2).toHaveURL(/\/verify-email$/)
      await expect(page2).not.toHaveURL(/token=/)

      const signInPromptLink = page2.getByRole('link', {
        name: TR('verifyEmail.signIn'),
      })
      await expect(signInPromptLink).toBeVisible()
      await expect(signInPromptLink).toHaveAttribute(
        'href',
        '/sign-in?returnUrl=%2Fverify-email',
      )
      const href = await signInPromptLink.getAttribute('href')
      expect(href).not.toContain('token')

      // 5. Sign in from the verification gate and confirm automatic verification return.
      await signInPromptLink.click()
      await expect(page2).toHaveURL(/\/sign-in\?returnUrl=%2Fverify-email/)
      await page2.locator('#sign-in-email').fill(email)
      await page2.locator('#sign-in-password').fill(INITIAL_PASSWORD)
      await page2.locator('form button[type="submit"]').click()

      await expect(page2).toHaveURL(/\/verify-email$/)
      await expect(page2.getByText(EN('verifyEmail.verifiedTitle'))).toBeVisible()
      await expect(page2.locator('html')).toHaveAttribute('lang', 'en')

      // 6. Reload /verify-email and confirm it remains verified.
      await page2.reload()
      await expect(page2.getByText(EN('verifyEmail.verifiedTitle'))).toBeVisible()

      // 7. Continue to app, sign out, sign in via header, and delete the throwaway account.
      await page2.getByRole('link', { name: EN('verifyEmail.continueToApp') }).click()
      await expect(page2).toHaveURL(/\/$/)

      await page2
        .getByRole('banner')
        .getByRole('button', { name: EN('header.signOut'), exact: true })
        .click()
      const headerSignIn = page2
        .getByRole('banner')
        .getByRole('button', { name: EN('header.signIn'), exact: true })
      await expect(headerSignIn).toBeVisible()

      await headerSignIn.click()
      await expect(page2).toHaveURL(/\/sign-in$/)
      await page2.locator('#sign-in-email').fill(email)
      await page2.locator('#sign-in-password').fill(INITIAL_PASSWORD)
      await page2.locator('form button[type="submit"]').click()

      await expect(
        page2
          .getByRole('banner')
          .getByRole('button', { name: EN('header.signOut'), exact: true }),
      ).toBeVisible()

      await page2.goto('/settings')
      await page2.getByTestId('delete-account').click()
      const dialog = page2.getByRole('dialog')
      await dialog
        .getByRole('button', { name: EN('accountSettings.deleteConfirm') })
        .click()

      await expect(page2).toHaveURL(/\/$/)
      await expect(
        page2
          .getByRole('banner')
          .getByRole('button', { name: EN('header.signIn'), exact: true }),
      ).toBeVisible()

      expect(rateLimited1).toEqual([])
      expect(rateLimited2).toEqual([])
    } finally {
      await context1.close()
      await context2.close()
    }
  })

  test('register → sign out → forgot-password → reset-password → delete account', async ({
    browser,
    request,
  }) => {
    const ip = '203.0.113.211'
    const email = uniqueEmail('single-ip-reset')
    const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)
    await mailpit.clear()

    const context = await browser.newContext()
    const page = await context.newPage()
    const rateLimited = await pinClientIp(page, ip)

    try {
      // 1. Register a unique throwaway account.
      await page.goto('/register')
      await page.locator('#register-email').fill(email)
      await page.locator('#register-password').fill(INITIAL_PASSWORD)
      await page.locator('#register-consent').check()
      await page.locator('form button[type="submit"]').click()
      await expect(page.getByTestId('registration-verification-notice')).toBeVisible()

      // 2. Sign out from header.
      await page
        .getByRole('banner')
        .getByRole('button', { name: TR('header.signOut'), exact: true })
        .click()
      await expect(
        page
          .getByRole('banner')
          .getByRole('button', { name: TR('header.signIn'), exact: true }),
      ).toBeVisible()

      // 3. Request password reset.
      await mailpit.clear()
      await page.goto('/reset-password')
      await page.locator('#password-reset-email').fill(email)
      await page.locator('form button[type="submit"]').click()
      await expect(page.getByTestId('password-reset-confirmation')).toBeVisible()

      // 4. Fetch MailPit reset link and set a new password.
      const { link } = await mailpit.waitForLink(/\/reset-password\?token=/, {
        description: 'the password-reset link',
      })
      await page.goto(link)
      await page.locator('#password-reset-password').fill(NEW_PASSWORD)
      await page.locator('form button[type="submit"]').click()
      await expect(page.getByTestId('password-reset-success')).toBeVisible()

      // 5. Delete the throwaway account cleanly via UI.
      await page.goto('/settings')
      await page.getByTestId('delete-account').click()
      const dialog = page.getByRole('dialog')
      await dialog
        .getByRole('button', { name: TR('accountSettings.deleteConfirm') })
        .click()

      await expect(page).toHaveURL(/\/$/)
      await expect(
        page
          .getByRole('banner')
          .getByRole('button', { name: TR('header.signIn'), exact: true }),
      ).toBeVisible()

      expect(rateLimited).toEqual([])
    } finally {
      await context.close()
    }
  })
})
