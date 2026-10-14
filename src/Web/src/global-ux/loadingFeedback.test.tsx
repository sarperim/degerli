import { describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'

import { StockListPage } from '@/routes/StockListPage'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  STOCK_LIST_ENVELOPE,
  STOCK_LIST_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'
import { renderScreener, resetScreenerTest } from '@/screens/screener/testSupport'

/**
 * TC-XC-012 (L3, MSW/throttled) — loading and pending feedback spot checks
 * (`.pipeline/testing/cross-cutting.md` §2; UXR-G-001/002, architecture M-5).
 *
 * Two classes, one surface each:
 *  - a screen load shows a visible loading indication until the content (or
 *    empty/error) state renders — asserted on the SCR-002 stock list;
 *  - a mutation's trigger shows a pending state and is locked against
 *    re-invocation (UXR-G-020) — asserted on the SCR-003 save-screen mutation.
 *
 * The handlers are gated (a manually released promise) rather than time-delayed
 * so the assertions are deterministic: the request cannot resolve until the test
 * says so, regardless of machine speed.
 */

const TR = (key: string) => catalogValue('tr', key)

function renderStockList(initialEntries: string[] = ['/stocks']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/stocks" element={<StockListPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('TC-XC-012 — loading and pending feedback (spot)', () => {
  it('SCR-002 shows a visible loading indication until the list content renders', async () => {
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.get(`${API_BASE}/stocks`, async () => {
        await gate
        return HttpResponse.json({ ...STOCK_LIST_ENVELOPE, stocks: STOCK_LIST_FIXTURE })
      }),
    )

    renderStockList()

    // Visible loading indication while the read is in flight (UXR-G-001) — and no
    // content yet, so the indicator is not vacuous.
    expect(
      await screen.findByRole('status', { name: TR('common.loading') }),
    ).toBeInTheDocument()
    expect(screen.queryByTestId('stock-row-ALFA')).not.toBeInTheDocument()

    release()

    // Content replaces the indicator once the read settles.
    expect(await screen.findByTestId('stock-row-ALFA')).toBeInTheDocument()
    await waitFor(() =>
      expect(
        screen.queryByRole('status', { name: TR('common.loading') }),
      ).not.toBeInTheDocument(),
    )
  })

  it('the save-screen trigger is pending and locked against re-invocation (UXR-G-020)', async () => {
    resetScreenerTest()
    const user = userEvent.setup()

    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    let saveCalls = 0
    server.use(
      http.post(`${API_BASE}/me/screens`, async () => {
        saveCalls += 1
        await gate
        return HttpResponse.json(
          { id: 1, name: 'Ucuz kaliteli', criteria: [], createdAt: '', updatedAt: '' },
          { status: 201 },
        )
      }),
    )

    renderScreener()
    await screen.findByTestId('metric-select')
    await user.selectOptions(screen.getByTestId('metric-select'), 'pe')
    await user.click(screen.getByTestId('add-criterion'))
    const row = screen.getAllByTestId('criterion-row').at(-1)!
    await user.selectOptions(within(row).getByTestId('criterion-bound'), 'max')
    await user.type(within(row).getByTestId('criterion-max'), '15')
    await user.type(screen.getByTestId('save-name'), 'Ucuz kaliteli')

    await user.click(screen.getByTestId('save-button'))

    // Pending: the trigger reflects the in-flight mutation and is disabled.
    const pending = await screen.findByRole('button', {
      name: TR('screener.saving'),
    })
    expect(pending).toBeDisabled()
    expect(screen.getByTestId('save-name')).toBeDisabled()

    // Re-invocation is locked: a second click while pending issues no second call.
    await user.click(pending)
    expect(saveCalls).toBe(1)

    release()
    await waitFor(() =>
      expect(
        screen.queryByRole('button', { name: TR('screener.saving') }),
      ).not.toBeInTheDocument(),
    )
    expect(saveCalls).toBe(1)
  })
})
