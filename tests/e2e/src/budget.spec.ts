import { expect, test } from '@playwright/test'

import config from '../playwright.config'
import { E2E_BUDGET_MS } from './config'

/**
 * Suite runtime budget gate (architecture §9; TC-XC-014): the Playwright run must
 * complete within 5 minutes. This guards the wiring in `playwright.config.ts`
 * against silent drift.
 */

test('e2e suite runtime budget gate is 5 minutes or less', () => {
  expect(E2E_BUDGET_MS).toBeLessThanOrEqual(5 * 60 * 1000)
  expect(config.globalTimeout).toBe(E2E_BUDGET_MS)
})
