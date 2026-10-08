import { randomUUID } from 'node:crypto'

import { expect, test } from '@playwright/test'

import { MAILPIT_BASE_URL } from './config'
import { MailpitClient, MailpitTimeoutError } from './helpers/mailpit'

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
