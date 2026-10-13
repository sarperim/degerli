import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import type { ScreenerRunRequest } from '@/lib/api/contracts'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  SCREENER_RUN_FIXTURE,
  problemHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { fill, renderScreener, resetScreenerTest } from '@/screens/screener/testSupport'

/**
 * TC-SCR-026 — Build and run anonymously (critical flow 1, L4).
 * TC-SCR-029 — Result → research → back (critical flow 4, L4).
 * TC-SCR-023 — Draft preservation across navigation and the auth hop (L3).
 */

const TR = (key: string) => catalogValue('tr', key)

async function addMetric(user: ReturnType<typeof userEvent.setup>, code: string) {
  await user.selectOptions(screen.getByTestId('metric-select'), code)
  await user.click(screen.getByTestId('add-criterion'))
}

function lastRow() {
  const rows = screen.getAllByTestId('criterion-row')
  return rows[rows.length - 1]
}

/** Add pe ≤ 15 and roe ≥ 15% through the builder UI. */
async function buildPeRoe(user: ReturnType<typeof userEvent.setup>) {
  await addMetric(user, 'pe')
  await user.selectOptions(within(lastRow()).getByTestId('criterion-bound'), 'max')
  await user.type(within(lastRow()).getByTestId('criterion-max'), '15')

  await addMetric(user, 'roe')
  await user.type(within(lastRow()).getByTestId('criterion-min'), '15')
}

beforeEach(resetScreenerTest)

describe('TC-SCR-026 — build and run anonymously', () => {
  it('runs P/E≤15 and ROE≥15%, converting percent input to canonical fractions', async () => {
    const user = userEvent.setup()
    let captured: ScreenerRunRequest | null = null
    server.use(
      http.post(`${API_BASE}/screener/run`, async ({ request }) => {
        captured = (await request.json()) as ScreenerRunRequest
        return HttpResponse.json(SCREENER_RUN_FIXTURE)
      }),
    )

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildPeRoe(user)

    await user.click(screen.getByTestId('run-button'))
    await screen.findByTestId('results')

    expect(captured).not.toBeNull()
    expect(captured!.criteria).toEqual([
      { metricCode: 'pe', bound: 'max', maxValue: 15 },
      { metricCode: 'roe', bound: 'min', minValue: 0.15 },
    ])

    // The matching stocks, counts and as-of date are shown.
    for (const symbol of ['ALFA', 'REST', 'UNSEC']) {
      expect(screen.getByTestId(`result-row-${symbol}`)).toBeInTheDocument()
    }
    expect(screen.getByTestId('match-count')).toHaveTextContent(
      fill(TR('screener.matchCount'), { count: 3 }),
    )
    expect(screen.getByTestId('excluded-count')).toHaveTextContent(
      fill(TR('screener.excludedCount'), { count: 2 }),
    )
  })

  it('opens the stock page when a result row link is activated', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')
    await buildPeRoe(user)
    await user.click(screen.getByTestId('run-button'))
    await screen.findByTestId('results')

    await user.click(
      screen.getByRole('link', { name: 'Alfa Teknoloji A.Ş. (ALFA)' }),
    )
    expect(await screen.findByTestId('stock-page-symbol')).toHaveTextContent('ALFA')
  })
})

describe('TC-SCR-029 — result → research → back', () => {
  it('preserves criteria, name and results across a trip to a stock page', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')
    await buildPeRoe(user)
    await user.type(screen.getByTestId('save-name'), 'Benim taramam')
    await user.click(screen.getByTestId('run-button'))
    await screen.findByTestId('results')

    await user.click(
      screen.getByRole('link', { name: 'Alfa Teknoloji A.Ş. (ALFA)' }),
    )
    await screen.findByTestId('stock-page')
    await user.click(screen.getByRole('link', { name: 'back-to-screener' }))

    // Results still present (not recomputed away), criteria and name intact.
    expect(await screen.findByTestId('result-row-ALFA')).toBeInTheDocument()
    expect(screen.getAllByTestId('criterion-row')).toHaveLength(2)
    expect(screen.getByTestId('save-name')).toHaveValue('Benim taramam')
  })
})

describe('TC-SCR-023 — draft preservation across the auth hop', () => {
  it('preserves criteria and entered name across the sign-in hop', async () => {
    const user = userEvent.setup()
    // Anonymous save attempt is rejected; the prompt carries the sign-in path.
    server.use(problemHandler(`${API_BASE}/me/screens`, 'UNAUTHENTICATED', { method: 'post' }))

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildPeRoe(user)
    await user.type(screen.getByTestId('save-name'), 'Ucuz kaliteli')

    await user.click(screen.getByTestId('save-button'))
    const prompt = await screen.findByTestId('save-anonymous')

    await user.click(within(prompt).getByRole('link', { name: TR('screener.signIn') }))
    expect(await screen.findByTestId('sign-in-page')).toBeInTheDocument()

    await user.click(screen.getByRole('link', { name: 'back-to-screener' }))

    // Returned with criteria and name intact.
    expect(await screen.findByTestId('metric-select')).toBeInTheDocument()
    expect(screen.getAllByTestId('criterion-row')).toHaveLength(2)
    expect(screen.getByTestId('save-name')).toHaveValue('Ucuz kaliteli')
  })
})
