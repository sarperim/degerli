import path from 'node:path'
import { fileURLToPath } from 'node:url'

/**
 * Shared e2e constants (test strategy §5.4, §9.1).
 *
 * URLs are overridable so the harness can target a non-default compose port; the
 * defaults match compose.ci.yml (API 8080, MailPit 8025) and the Playwright
 * webServer's SPA origin (5173).
 */

const here = path.dirname(fileURLToPath(import.meta.url))

/**
 * The hard suite runtime budget (architecture §9 / TC-XC-014): the Playwright run
 * must complete within 5 minutes. `playwright.config.ts` wires this to
 * `globalTimeout`; `budget.spec.ts` fails if the wiring drifts.
 */
export const E2E_BUDGET_MS = 5 * 60 * 1000

/** The compose.ci API origin (`GET /health`). */
export const API_BASE_URL = process.env.E2E_API_BASE_URL ?? 'http://127.0.0.1:8080'

/** The compose.ci MailPit HTTP API/UI origin (message retrieval). */
export const MAILPIT_BASE_URL =
  process.env.E2E_MAILPIT_BASE_URL ?? 'http://127.0.0.1:8025'

/** The SPA origin served by the Playwright webServer. */
export const WEB_BASE_URL = process.env.E2E_WEB_BASE_URL ?? 'http://127.0.0.1:5173'

/** Repository root (tests/e2e/src → repo). */
export const REPO_ROOT = path.resolve(here, '../../..')

/** The checked-in i18n catalogs — UI copy is asserted from the repo, never hardcoded. */
export const WEB_CATALOG_DIR = path.join(REPO_ROOT, 'src/Web/src/i18n/catalogs')
