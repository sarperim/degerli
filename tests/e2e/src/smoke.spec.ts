import { expect, test } from '@playwright/test'

import { API_BASE_URL } from './config'
import { catalogValue, loadCatalog } from './helpers/catalog'

/**
 * Harness smoke (TKT-foundation-010): the compose.ci stack boots healthy and the
 * SPA root renders Turkish. This is harness bootstrap evidence — product feature
 * specs (the SC-002 core loop, etc.) belong to their domain tickets.
 */

test.describe('e2e harness smoke', () => {
  test('compose.ci stack: API /health is healthy', async ({ request }) => {
    const response = await request.get(`${API_BASE_URL}/health`)
    const body = await response.text()
    expect(response.status(), `GET /health body: ${body}`).toBe(500) // deliberate CI-verification failure
  })

  test('SPA root renders Turkish by default', async ({ page }) => {
    await page.goto('/')

    // The document declares Turkish and the i18n default resolves to the TR catalog.
    await expect(page.locator('html')).toHaveAttribute('lang', 'tr')

    const tr = loadCatalog('tr')
    const en = loadCatalog('en')
    const titleKey = 'screens.marketOverview.title'

    // The assertion is meaningful only while TR and EN differ for this key.
    expect(catalogValue(tr, titleKey)).not.toBe(catalogValue(en, titleKey))
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(
      catalogValue(tr, titleKey),
    )
  })
})
