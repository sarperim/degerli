import { expect, type Page } from '@playwright/test'

/**
 * Shared SCR-003 screener driving helpers for the system-level e2e specs
 * (TC-XC-001, TC-XC-008, TC-RES-028).
 *
 * These spec files span domains (dashboard → screener → stock page → DCF), so
 * they share one small, UI-driven screener builder rather than each re-deriving
 * the criteria-builder interactions. The helper reads no fixture prose — it fills
 * the user-entered bounds the analysis phrases in plain language and returns the
 * canonical run payload the API served for the run.
 */

export interface ScreenerRunRow {
  symbol: string
  name: string
  sector: string | null
  values: Record<string, number | null>
}

/** POST /api/v1/screener/run response shape (the fields these specs assert). */
export interface ScreenerRunResponse {
  asOf: string
  stale: boolean
  matchCount: number
  excludedCount: number
  droppedCriteria: string[]
  rows: ScreenerRunRow[]
}

/** A criterion bound as the UI builder presents it. */
export type CriterionBound = 'min' | 'max' | 'range'

/**
 * Adds one criterion to the SCR-003 builder through the real UI: pick the metric
 * from the catalog select, add the row, set the bound, then fill the displayed
 * bound value(s) (percentage points for `%` metrics — the API boundary converts).
 */
export async function addCriterion(
  page: Page,
  metricCode: string,
  bound: CriterionBound,
  value: string,
): Promise<void> {
  await page.getByTestId('metric-select').selectOption(metricCode)
  await page.getByTestId('add-criterion').click()

  const row = page.locator(
    `[data-testid="criterion-row"][data-metric="${metricCode}"]`,
  )
  await expect(row).toBeVisible()
  await row.getByTestId('criterion-bound').selectOption(bound)

  if (bound === 'min' || bound === 'range') {
    await row.getByTestId('criterion-min').fill(value)
  }
  if (bound === 'max' || bound === 'range') {
    await row.getByTestId('criterion-max').fill(value)
  }
}

/**
 * Runs the built criteria and returns the canonical `POST /screener/run` payload.
 * The click and the response are awaited together so the returned payload is the
 * exact one the UI rendered (the cross-surface equality sweep compares them).
 */
export async function runScreener(page: Page): Promise<ScreenerRunResponse> {
  const [response] = await Promise.all([
    page.waitForResponse(
      (candidate) =>
        candidate.url().includes('/api/v1/screener/run') &&
        candidate.request().method() === 'POST',
    ),
    page.getByTestId('run-button').click(),
  ])
  expect(response.status()).toBe(200)
  return (await response.json()) as ScreenerRunResponse
}

/**
 * The ALFA / P/E ≤ 15 AND ROE ≥ 15% criterion set the analysis uses as the
 * canonical SC-002 screen (TC-SCR-002 golden: 3 matches, 2 excluded).
 */
export async function buildCanonicalScreen(page: Page): Promise<void> {
  await addCriterion(page, 'pe', 'max', '15')
  await addCriterion(page, 'roe', 'min', '15')
}

/** Parses a rendered number that may carry a unit suffix (`10x`, `20,00`, `10`). */
export function parseRenderedNumber(text: string): number {
  const normalized = text.replace(',', '.').replace(/[^\d.-]/g, '')
  return Number(normalized)
}
