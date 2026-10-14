import { expect, test, type Page } from '@playwright/test'

import { formatAxeViolations, runAxe, signInWithFixture } from './helpers/globalUx'
import { tagWithClientIp } from './helpers/accounts'

/**
 * TC-XC-017 (L4, axe-core) — accessibility automation on the key surfaces
 * (`.pipeline/testing/cross-cutting.md` §2; UXR-G-026/027/028; strategy
 * decision C: axe-core embedded in Playwright, zero-known-violations gate).
 *
 * The eight key surfaces — SCR-001/002/003/005/006/007/010/012 — are scanned in
 * their populated states. SCR-010 (Account Settings) and SCR-012 (Admin) require
 * a session, so the scan signs in once as the seeded builder account (verified
 * and builder-role, FU §9). Findings are treated as defects, never suppressed.
 */

const BUILDER_EMAIL = 'builder@degerli.test'

test.beforeEach(async ({ page }) => {
  tagWithClientIp(page)
})

interface Surface {
  scr: string
  path: string
  /** A marker that only renders once the surface's data has populated. */
  ready: (page: Page) => Promise<void>
}

const SURFACES: Surface[] = [
  {
    scr: 'SCR-001',
    path: '/',
    ready: async (page) => {
      await expect(page.getByTestId('index-xu100')).toBeVisible()
    },
  },
  {
    scr: 'SCR-002',
    path: '/stocks',
    ready: async (page) => {
      await expect(page.getByTestId('stock-row-ALFA')).toBeVisible()
    },
  },
  {
    scr: 'SCR-003',
    path: '/screener',
    ready: async (page) => {
      await expect(page.getByTestId('metric-select')).toBeVisible()
    },
  },
  {
    scr: 'SCR-005',
    path: '/stocks/ALFA',
    ready: async (page) => {
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    },
  },
  {
    scr: 'SCR-006',
    path: '/stocks/ALFA/dcf',
    ready: async (page) => {
      await expect(page.getByTestId('dcf-params')).toBeVisible()
      await expect(page.getByTestId('dcf-fair-value')).toBeVisible()
    },
  },
  {
    scr: 'SCR-007',
    path: '/register',
    ready: async (page) => {
      await expect(page.locator('#register-email')).toBeVisible()
    },
  },
  {
    scr: 'SCR-010',
    path: '/settings',
    ready: async (page) => {
      await expect(page.getByTestId('settings-language')).toBeVisible()
    },
  },
  {
    scr: 'SCR-012',
    path: '/admin',
    ready: async (page) => {
      await expect(page.getByTestId('ops-summary')).toBeVisible()
    },
  },
]

test.describe('TC-XC-017 — axe-core zero-known-violations gate', () => {
  test('the eight key surfaces report zero accessibility violations', async ({
    page,
  }) => {
    tagWithClientIp(page)
    await signInWithFixture(page, BUILDER_EMAIL)

    for (const surface of SURFACES) {
      await page.goto(surface.path)
      await surface.ready(page)
      const violations = await runAxe(page)
      expect(
        violations,
        `${surface.scr} (${surface.path}) axe violations:\n${formatAxeViolations(violations)}`,
      ).toEqual([])
    }
  })
})
