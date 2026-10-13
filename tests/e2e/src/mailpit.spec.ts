import { randomUUID } from 'node:crypto'

import { expect, test } from '@playwright/test'

import { API_BASE_URL, MAILPIT_BASE_URL } from './config'
import { extractLinks, MailpitClient, MailpitTimeoutError } from './helpers/mailpit'

/**
 * MailPit helper acceptance (TKT-foundation-010): the helper retrieves a message
 * (and its link) sent by a probe flow, with bounded polling and an explicit error
 * on timeout. Assertions are on payload values (subject/body/link), never on
 * wall-clock dates (FU §8.3).
 */

test.describe('MailPit helper (bounded polling)', () => {
  test('retrieves a probe message and its verification link', async ({ request }) => {
    const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)
    await mailpit.clear()

    const token = randomUUID()
    const subject = `degerli-probe ${token}`
    const link = `http://127.0.0.1:5173/verify?token=${token}`

    await mailpit.sendProbe({
      from: 'probe@degerli.local',
      to: 'inbox@degerli.local',
      subject,
      text: `Doğrulama bağlantısı: ${link}`,
    })

    const message = await mailpit.waitForMessage(
      (candidate) => candidate.Subject === subject,
      { description: `subject "${subject}"` },
    )
    expect(message.Subject).toBe(subject)

    const full = await mailpit.get(message.ID)
    expect(full.Text).toContain(token)

    const retrieved = await mailpit.waitForLink(/\/verify\?token=/, {
      description: 'a verification link',
    })
    expect(retrieved.link).toBe(link)
  })

  test('times out with an explicit error when no message arrives', async ({ request }) => {
    const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)
    await mailpit.clear()

    const error = await mailpit
      .waitForMessage(() => false, {
        timeoutMs: 400,
        intervalMs: 50,
        description: 'the absent message',
      })
      .then(
        () => undefined,
        (thrown: unknown) => thrown,
      )

    expect(error).toBeInstanceOf(MailpitTimeoutError)
    expect((error as Error).message).toContain('within 400 ms')
    expect((error as Error).message).toContain('the absent message')
  })
})

/**
 * TKT-foundation-014 — transactional-mail delivery end to end. When `Smtp__Host`
 * is configured (compose.ci points it at `mailpit`), the API resolves the real SMTP
 * dispatcher behind the mail seam, so an API-initiated password-reset request must
 * actually reach MailPit through the SMTP relay — not merely the structured log.
 * This API-driven probe is the harness-level regression guard for that path (and
 * the delivery precondition TC-ACC-030 depends on).
 */
test.describe('Transactional mail delivery (API → SMTP → MailPit)', () => {
  // A seeded, registered, verified account (FU §9): its reset request dispatches.
  const SEEDED_EMAIL = 'user-a@degerli.test'
  // A dedicated client IP (TEST-NET-1) so this probe never shares the 5/min /auth
  // rate-limit bucket with a browser spec (01 §10, test-controllable X-Forwarded-For):
  // sign-in.spec uses 203.0.113.x and admin/account-settings use 198.51.100.x.
  const CLIENT_IP = '192.0.2.77'

  test('a password-reset request is delivered to MailPit through the real API', async ({ request }) => {
    const mailpit = new MailpitClient(request, MAILPIT_BASE_URL)
    await mailpit.clear()

    // The antiforgery cookie is issued by this GET and echoed as X-CSRF-Token on
    // the POST (`03` §2); the request context carries the cookie across calls.
    const csrfResponse = await request.get(`${API_BASE_URL}/api/v1/auth/csrf-token`, {
      headers: { 'X-Forwarded-For': CLIENT_IP },
    })
    expect(csrfResponse.ok()).toBe(true)
    const { token } = (await csrfResponse.json()) as { token: string }
    expect(token).toBeTruthy()

    const forgotResponse = await request.post(
      `${API_BASE_URL}/api/v1/auth/forgot-password`,
      {
        headers: { 'X-CSRF-Token': token, 'X-Forwarded-For': CLIENT_IP },
        data: { email: SEEDED_EMAIL },
      },
    )
    // Enumeration-neutral 200 for the registered address (NFR-ACC-002).
    expect(forgotResponse.status()).toBe(200)

    // Bounded MailPit polling (≤ 10 s), never a bare sleep (test strategy §15).
    const message = await mailpit.waitForMessage(
      (candidate) => candidate.To.some((to) => to.Address === SEEDED_EMAIL),
      { description: `the reset e-mail delivered to ${SEEDED_EMAIL}` },
    )
    // Assert an ASCII fragment of the fixed subject, avoiding coupling to the
    // exact Turkish copy or its transport encoding.
    expect(message.Subject).toContain('Reset your password')

    // The delivered body carries the single-use reset link (UC-ACC-004).
    const full = await mailpit.get(message.ID)
    expect(
      extractLinks(full).some((link) => /\/reset-password\?token=/.test(link)),
    ).toBe(true)
  })
})
