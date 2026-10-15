import { expect, test } from '@playwright/test'

import { catalogValue, loadCatalog } from './helpers/catalog'
import {
  activeElementName,
  expectVisibleFocus,
  focusByTab,
  signInWithFixture,
} from './helpers/globalUx'
import { tagWithClientIp } from './helpers/accounts'

/**
 * TC-XC-020 (L4) — keyboard operability and focus management
 * (`.pipeline/testing/cross-cutting.md` §2; UXR-G-026/027/028).
 *
 * A keyboard-only user traverses SCR-003 (build criteria → run → open a result)
 * and SCR-006 (adjust a parameter → recompute → save prompt) with visible focus
 * throughout; every focused control carries a programmatically determinable
 * label; and a dialog traps focus and restores it on close.
 */

const tr = loadCatalog('tr')
const TR = (key: string) => catalogValue(tr, key)

const VERIFIED_USER = 'user-a@degerli.test'

test.beforeEach(async ({ page }) => {
  tagWithClientIp(page)
})

test.describe('TC-XC-020 — keyboard operability and focus management', () => {
  test('SCR-003: build → run → open a result, keyboard-only', async ({ page }) => {
    await page.goto('/screener')
    await expect(page.getByTestId('metric-select')).toBeVisible()

    // Metric picker is keyboard-reachable; choose a metric with the arrow key.
    await focusByTab(page, '[data-testid="metric-select"]')
    await expectVisibleFocus(page)
    expect(await activeElementName(page)).not.toBe('')
    await page.keyboard.press('ArrowDown')
    await expect(page.getByTestId('add-criterion')).toBeEnabled()

    // Add the criterion.
    await focusByTab(page, '[data-testid="add-criterion"]')
    await page.keyboard.press('Enter')
    await expect(page.getByTestId('criterion-row')).toHaveCount(1)

    // Change the bound to `max` and enter a value.
    await focusByTab(page, '[data-testid="criterion-bound"]')
    await page.keyboard.press('ArrowDown')
    await focusByTab(page, '[data-testid="criterion-max"]')
    await page.keyboard.type('15')

    // Run with the keyboard and open the first result.
    await focusByTab(page, '[data-testid="run-button"]')
    await page.keyboard.press('Enter')
    await expect(page.getByTestId('results')).toBeVisible()

    await focusByTab(page, 'a[href^="/stocks/"]')
    await expectVisibleFocus(page)
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/stocks\/[A-Z]+$/)
  })

  test('SCR-006: adjust → recompute → save prompt, keyboard-only', async ({ page }) => {
    await page.goto('/stocks/ALFA/dcf')
    await expect(page.getByTestId('dcf-params')).toBeVisible()

    await focusByTab(page, '[data-testid="dcf-param-discount_rate"]')
    await expectVisibleFocus(page)
    expect(await activeElementName(page)).not.toBe('')
    await page.keyboard.press('Control+A')
    await page.keyboard.type('17')
    await expect(page.getByTestId('dcf-param-discount_rate')).toHaveValue('17')
    // The change marks the baseline as modified (recompute is debounced).
    await expect(page.getByTestId('dcf-unsaved')).toBeVisible()

    // Reach the save prompt with the keyboard: name the scenario (the save
    // control stays disabled until named), then activate it.
    await focusByTab(page, '[data-testid="dcf-scenario-name"]')
    await page.keyboard.type('Klavye senaryosu')
    await focusByTab(page, '[data-testid="dcf-scenario-save"]')
    await expectVisibleFocus(page)
    await page.keyboard.press('Enter')
    await expect(page.getByTestId('dcf-scenario-anonymous')).toBeVisible()
  })

  test('a dialog traps focus and restores it to the trigger on close', async ({
    page,
  }) => {
    tagWithClientIp(page)
    await signInWithFixture(page, VERIFIED_USER)
    await page.goto('/settings')
    await expect(page.getByTestId('settings-deletion')).toBeVisible()

    await focusByTab(page, '[data-testid="delete-account"]')
    await page.keyboard.press('Enter')

    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()

    // Focus is trapped inside the dialog.
    await expect
      .poll(() =>
        page.evaluate(() =>
          document.activeElement?.closest('[role="dialog"]') !== null,
        ),
      )
      .toBe(true)
    for (let i = 0; i < 6; i += 1) {
      await page.keyboard.press('Tab')
      expect(
        await page.evaluate(() =>
          document.activeElement?.closest('[role="dialog"]') !== null,
        ),
      ).toBe(true)
    }

    // Escape closes it and focus returns to the trigger.
    await page.keyboard.press('Escape')
    await expect(dialog).not.toBeVisible()
    await expect(page.getByTestId('delete-account')).toBeFocused()
  })
})
