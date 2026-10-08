import { expect, test } from '@playwright/test'

import { assertNetworkQuiet, recordRequests } from './helpers/network'

/**
 * Network-quiet helper acceptance (TKT-foundation-010): the helper reports a
 * genuine quiet window and — crucially — is proven non-vacuous by a positive
 * control that deliberately triggers a matching request.
 */

test.describe('network-quiet helper', () => {
  test('reports a quiet SPA root (no /api requests during the window)', async ({ page }) => {
    await page.goto('/')
    await assertNetworkQuiet(page, { windowMs: 1_000 })
  })

  test('positive control: detects a matching request during the window', async ({ page }) => {
    await page.goto('/')

    const recorder = recordRequests(page, /\/stocks/)
    await page.goto('/stocks')
    const requests = recorder.stop()

    expect(requests.length).toBeGreaterThan(0)
  })
})
