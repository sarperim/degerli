import { expect, test } from '@playwright/test'

import { assertNetworkQuiet, recordRequests } from './helpers/network'

/**
 * Network-quiet helper acceptance (TKT-foundation-010): the helper reports a
 * genuine quiet window and — crucially — is proven non-vacuous by a positive
 * control that deliberately triggers a matching request.
 */

test.describe('network-quiet helper', () => {
  test('reports a quiet SPA root (no /api requests during the window)', async ({ page }) => {
    // Wait for the SPA's initial data load to settle before opening the quiet
    // window (TKT-mov-006): starting the window at `goto` caught the first
    // render's own fetches, which are load, not polling. The window must guard
    // against periodic data requests implying real-time updates (TC-XC-021/
    // TC-MOV-022), so it is measured after load.
    await page.goto('/', { waitUntil: 'networkidle' })
    // The global header reads the session once on mount — the control-plane read
    // that powers the sign-in/sign-out state (M-9, TKT-acc-008). The quiet window
    // guards against *data* polling implying real-time updates (TC-XC-021), so the
    // one-time session read is excluded; any other /api request still fails.
    await assertNetworkQuiet(page, {
      windowMs: 1_000,
      match: /\/api\/v1\/(?!auth\/session)/,
    })
  })

  test('positive control: detects a matching request during the window', async ({ page }) => {
    await page.goto('/')

    const recorder = recordRequests(page, /\/stocks/)
    await page.goto('/stocks')
    const requests = recorder.stop()

    expect(requests.length).toBeGreaterThan(0)
  })
})
