import { randomUUID } from 'node:crypto'

import { expect, type APIRequestContext, type Page } from '@playwright/test'

import { MAILPIT_BASE_URL } from '../config'
import { MailpitClient } from './mailpit'

/**
 * Shared L4 account-flow helpers (TKT-int-005, gated-save lifecycle).
 *
 * The gated-save lifecycle specs all provision their own throwaway accounts through
 * the real register → e-mail-verification flow rather than reusing the shared
 * seeded fixtures. That keeps them order-independent: `account-settings.spec.ts`
 * (TC-ACC-032) deletes the seeded `user-c`, so a spec that depended on a seeded
 * verified account would be sequence-sensitive against a single un-reseeded
 * `compose.ci` stack (determinism rule: no order dependence).
 *
 * The `/auth/*` budget is 5/min keyed by client IP; the browser's requests all
 * share the SPA proxy's address, so every request is tagged with its own
 * `X-Forwarded-For` (I-XC-2: the limiter keys on a test-controllable IP outside
 * production) exactly as the sibling specs do.
 */

/** A password that satisfies the registration policy (≥ 10 chars). */
export const NEW_ACCOUNT_PASSWORD = 'GatedSave1!long'

let ipCounter = 0

/** Give every browser request its own rate-limit bucket (see the file header). */
export function tagWithClientIp(page: Page): void {
  void page.route('**/api/v1/**', (route) => {
    ipCounter += 1
    return route.continue({
      headers: {
        ...route.request().headers(),
        'X-Forwarded-For': `10.99.${(ipCounter % 200) + 1}.${((ipCounter / 200) | 0) + 1}`,
      },
    })
  })
}

/** A unique throwaway address so parallel/sequential specs never collide. */
export function uniqueEmail(prefix: string): string {
  return `${prefix}-${randomUUID()}@example.test`
}

/**
 * Fill and submit the SCR-007 register form by its stable DOM ids (language
 * independent). Optionally carries a `?returnUrl` so the post-auth "return to
 * work" link targets the caller's surface.
 */
export async function fillRegister(
  page: Page,
  email: string,
  options: { returnUrl?: string } = {},
): Promise<void> {
  await page.locator('#register-email').fill(email)
  await page.locator('#register-password').fill(NEW_ACCOUNT_PASSWORD)
  await page.locator('#register-consent').check()
  await page.locator('form button[type="submit"]').click()
  await expect(page.getByTestId('registration-verification-notice')).toBeVisible()
}

/** Navigate to SCR-007 (optionally with a return target) and register; signs the caller in. */
export async function registerAccount(
  page: Page,
  email: string,
  options: { returnUrl?: string } = {},
): Promise<void> {
  const path = options.returnUrl
    ? `/register?returnUrl=${encodeURIComponent(options.returnUrl)}`
    : '/register'
  await page.goto(path)
  await fillRegister(page, email, options)
}

/** Fill and submit the SCR-008 sign-in form by its stable DOM ids. */
export async function fillSignIn(
  page: Page,
  email: string,
  password = NEW_ACCOUNT_PASSWORD,
): Promise<void> {
  await page.locator('#sign-in-email').fill(email)
  await page.locator('#sign-in-password').fill(password)
  await page.locator('form button[type="submit"]').click()
}

/** A MailPit client bound to the caller's API request context. */
export function mailpitFor(request: APIRequestContext): MailpitClient {
  return new MailpitClient(request, MAILPIT_BASE_URL)
}

/**
 * Bounded wait (≤ 10 s) for the SCR-007 verification e-mail and its single-use
 * link. The mailbox must be cleared by the caller beforehand so only the message
 * under test is visible.
 */
export async function waitForVerificationLink(
  request: APIRequestContext,
  email: string,
): Promise<string> {
  const mailpit = mailpitFor(request)
  const { link } = await mailpit.waitForLink(/\/verify-email\?token=/, {
    description: `the verification link delivered to ${email}`,
  })
  return link
}

/**
 * Build one valid screener criterion (P/E at most `max`) through the SCR-003
 * builder and give the screen a name, mirroring a save-ready draft.
 */
export async function buildScreenerSaveDraft(
  page: Page,
  screenName: string,
  max = '15',
): Promise<void> {
  await expect(page.getByTestId('metric-select')).toBeVisible()
  await page.getByTestId('metric-select').selectOption('pe')
  await page.getByTestId('add-criterion').click()
  const row = page.getByTestId('criterion-row').last()
  await row.getByTestId('criterion-bound').selectOption('max')
  await row.getByTestId('criterion-max').fill(max)
  await page.getByTestId('save-name').fill(screenName)
}
