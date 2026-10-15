import { createRequire } from 'node:module'

import { expect, type Page } from '@playwright/test'

import { catalogValue, loadCatalog } from './catalog'

/**
 * Shared L4 helpers for the global-behaviour sweep (TKT-int-003).
 *
 * - {@link switchLanguage} drives the header toggle in the language that is
 *   currently active, so the accessible button name is always read from the
 *   checked-in catalog rather than hardcoded (§8.3).
 * - {@link runAxe} injects the adopted `axe-core` build into the live page and
 *   returns its violations (strategy decision C, browser leg) — the jsdom leg in
 *   `src/Web` disables colour-contrast, so the real-browser pass is the only
 *   place that rule runs.
 */

const require = createRequire(import.meta.url)
const AXE_PATH = require.resolve('axe-core/axe.min.js')

export interface AxeViolation {
  id: string
  help: string
  nodes: unknown[]
}

interface AxeWindow {
  axe: {
    run: (
      context: Document,
      options?: unknown,
    ) => Promise<{ violations: AxeViolation[] }>
  }
}

/** Run axe-core against the current document; empty array === zero violations. */
export async function runAxe(page: Page): Promise<AxeViolation[]> {
  await page.addScriptTag({ path: AXE_PATH })
  return page.evaluate(async () => {
    const { axe } = window as unknown as AxeWindow
    const results = await axe.run(document, { resultTypes: ['violations'] })
    return results.violations
  })
}

/** Human-readable summary for a failed accessibility assertion. */
export function formatAxeViolations(violations: AxeViolation[]): string {
  return violations
    .map((v) => `${v.id}: ${v.help} (${v.nodes.length} node/s)`)
    .join('\n')
}

/** Switch the active language through the header toggle; a no-op if already active. */
export async function switchLanguage(page: Page, to: 'tr' | 'en'): Promise<void> {
  const current: 'tr' | 'en' =
    (await page.locator('html').getAttribute('lang')) === 'en' ? 'en' : 'tr'
  if (current === to) return

  const catalog = loadCatalog(current)
  const label = catalogValue(
    catalog,
    to === 'en' ? 'header.english' : 'header.turkish',
  )

  await page
    .getByRole('group', { name: catalogValue(catalog, 'header.languageToggle') })
    .getByRole('button', { name: label })
    .click()

  await expect(page.locator('html')).toHaveAttribute('lang', to)
}

/**
 * Advance focus with Tab until the active element matches `selector` (keyboard-only
 * navigation). Fails loudly if the control is unreachable, which is itself a
 * keyboard-operability defect (UXR-G-026).
 */
export async function focusByTab(
  page: Page,
  selector: string,
  maxTabs = 80,
): Promise<void> {
  for (let i = 0; i < maxTabs; i += 1) {
    await page.keyboard.press('Tab')
    const matched = await page.evaluate(
      (sel) => document.activeElement?.matches(sel) ?? false,
      selector,
    )
    if (matched) return
  }
  throw new Error(`focusByTab: "${selector}" unreachable within ${maxTabs} tabs`)
}

/** The active element must carry a programmatically determinable name. */
export async function activeElementName(page: Page): Promise<string> {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null
    if (!el) return ''
    const labelled = (el as HTMLInputElement).labels?.[0]?.textContent?.trim()
    return el.getAttribute('aria-label')?.trim() || labelled || el.textContent?.trim() || ''
  })
}

/** Assert the keyboard-focused element shows visible focus (:focus-visible). */
export async function expectVisibleFocus(page: Page): Promise<void> {
  const visible = await page.evaluate(
    () => document.activeElement?.matches(':focus-visible') ?? false,
  )
  expect(visible, 'keyboard-focused element must show visible focus').toBe(true)
}

/** The fixture password shared by every seeded account (FU §9). */
export const FIXTURE_PASSWORD = 'FixturePass1!'

/**
 * Sign in a seeded account and wait until the authenticated header state is
 * reflected, so the caller never races the redirect/session read.
 */
export async function signInWithFixture(page: Page, email: string): Promise<void> {
  await page.goto('/sign-in')
  await page.locator('#sign-in-email').fill(email)
  await page.locator('#sign-in-password').fill(FIXTURE_PASSWORD)
  await page.locator('form button[type="submit"]').click()
  await expect(
    page.getByRole('button', { name: /Çıkış yap|Sign out/ }),
  ).toBeVisible()
}


