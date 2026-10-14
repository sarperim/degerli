import { describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'

import { StockListPage } from '@/routes/StockListPage'
import type { SupportedLanguage } from '@/i18n'
import { catalogValue } from '@/test/i18n'
import { API_BASE, problemHandler } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'

/**
 * TC-XC-013 (L3, MSW 5xx) — plain-language errors; no technical leakage
 * (`.pipeline/testing/cross-cutting.md` §2; UXR-G-003/004, `03` §7 INTERNAL).
 *
 * A forced 500 on the SCR-002 read renders the generic, localized error with a
 * retry control; nothing technical (stack trace, raw exception, HTTP status,
 * trace identifier) reaches the DOM; and the user's input survives the failure.
 * The read surface is the stock list because it is an input-bearing screen, so
 * the "input preserved" half (UXR-G-004) is observable without a mutation.
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

function renderStockList(language: SupportedLanguage = 'tr') {
  return renderWithProviders(
    <MemoryRouter initialEntries={['/stocks']}>
      <Routes>
        <Route path="/stocks" element={<StockListPage />} />
      </Routes>
    </MemoryRouter>,
    { language },
  )
}

/** Every technical marker that must never appear in a user-facing error. */
const TECHNICAL_MARKERS =
  /INTERNAL|Internal error|HTTP\s*500|\b500\b|Exception|Stack|traceId|application\/problem|at \w+\./

describe('TC-XC-013 — plain-language errors; no technical leakage', () => {
  it('renders the generic Turkish error with retry, no leakage, input preserved', async () => {
    const user = userEvent.setup()
    // A distinctive trace identifier: proving it is absent also proves the raw
    // problem payload (which carries it) is never rendered.
    const traceId = '00-trace-LEAK00000000000000-01'
    server.use(
      problemHandler(`${API_BASE}/stocks`, 'INTERNAL', { method: 'get', traceId }),
    )

    renderStockList('tr')
    await user.type(screen.getByLabelText(TR('stockList.searchLabel')), 'beta')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(TR('stockList.errorTitle'))
    expect(alert).toHaveTextContent(TR('stockList.errorBody'))
    expect(
      within(alert).getByRole('button', { name: TR('common.retry') }),
    ).toBeInTheDocument()

    // Plain language only: no status code, exception, stack or identifier.
    expect(alert.textContent ?? '').not.toMatch(TECHNICAL_MARKERS)
    expect(alert.textContent ?? '').not.toContain(traceId)

    // Input preserved on failure (UXR-G-004).
    expect(screen.getByLabelText(TR('stockList.searchLabel'))).toHaveValue('beta')
  })

  it('renders the error and retry in the active (English) language', async () => {
    server.use(problemHandler(`${API_BASE}/stocks`, 'INTERNAL', { method: 'get' }))

    renderStockList('en')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(EN('stockList.errorTitle'))
    expect(alert).toHaveTextContent(EN('stockList.errorBody'))
    expect(
      within(alert).getByRole('button', { name: EN('common.retry') }),
    ).toBeInTheDocument()
    expect(alert.textContent ?? '').not.toMatch(TECHNICAL_MARKERS)
  })
})
