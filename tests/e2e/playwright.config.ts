import path from 'node:path'
import { fileURLToPath } from 'node:url'

import { defineConfig, devices } from '@playwright/test'

import { E2E_BUDGET_MS, WEB_BASE_URL } from './src/config'

/**
 * L4 Playwright project (architecture §6.1 e2e job; test strategy §5.4, §9.1).
 *
 * - Chromium only; fixed browser locale `tr-TR` and timezone `Europe/Istanbul`
 *   so now-anchored fixture offsets render deterministically (FU §8.3).
 * - The API/Postgres/MailPit stack is compose.ci.yml, brought up by the caller
 *   (the CI e2e job, or `npm run stack:up`). This config only starts the SPA.
 * - `globalTimeout` is the hard 5-minute suite budget (TC-XC-014).
 * - `retries: 0`: the test strategy treats a flaky test as a defect (§10) —
 *   failures are never retry-masked.
 */

const here = path.dirname(fileURLToPath(import.meta.url))
const webRoot = path.resolve(here, '../../src/Web')

export default defineConfig({
  testDir: './src',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: process.env.CI
    ? [['list'], ['html', { open: 'never' }]]
    : [['list']],
  globalTimeout: E2E_BUDGET_MS,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: WEB_BASE_URL,
    locale: 'tr-TR',
    timezoneId: 'Europe/Istanbul',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    // SPA is not a compose.ci service (the stack under test is API + data
    // platform, §6.1); the harness serves the production build on 5173.
    command: 'npm run build && npm run preview -- --port 5173 --strictPort',
    cwd: webRoot,
    url: WEB_BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
})
