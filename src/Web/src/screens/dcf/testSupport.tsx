/* eslint-disable react-refresh/only-export-components -- test harness: stub
   route components + render helpers intentionally share one test-only module. */
import { Link, MemoryRouter, Route, Routes, useParams } from 'react-router'

import { DcfCalculatorScreen } from '@/screens/dcf/DcfCalculatorScreen'
import { renderWithProviders } from '@/test/render'
import {
  dcfBaselineHandler,
  dcfComputeHandler,
  scenariosListHandler,
  sessionHandler,
  VERIFIED_SESSION_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import type { SupportedLanguage } from '@/i18n'
import { useDraftStore } from '@/store/draftStore'

/**
 * Shared harness for the SCR-006 component tests (TKT-val-006). A stub stock
 * page lets the critical flow open the calculator from the stock page in-memory;
 * the default MSW handlers return the ALFA baseline and the compute golden.
 */

function StockPageStub() {
  const { symbol } = useParams<{ symbol: string }>()
  return (
    <div data-testid="stock-page">
      <span data-testid="stock-page-symbol">{symbol}</span>
      <Link to={`/stocks/${symbol}/dcf`} data-testid="open-dcf">
        open-dcf
      </Link>
    </div>
  )
}

export function renderDcf(
  initialEntries: string[] = ['/stocks/ALFA/dcf'],
  language: SupportedLanguage = 'tr',
) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
        <Route path="/stocks/:symbol/dcf" element={<DcfCalculatorScreen />} />
        <Route path="/sign-in" element={<div data-testid="sign-in-page" />} />
        <Route path="/register" element={<div data-testid="register-page" />} />
        <Route path="/settings" element={<div data-testid="settings-page" />} />
      </Routes>
    </MemoryRouter>,
    { language },
  )
}

/**
 * Reset the persisted draft store and register the default DCF handlers. The
 * session defaults to a signed-in account so the scenario picker (now
 * auth-gated, mirroring SCR-004) exercises its populated/empty states; tests
 * override it with `sessionHandler(ANONYMOUS_SESSION_FIXTURE)` when needed.
 */
export function resetDcfTest() {
  sessionStorage.clear()
  useDraftStore.getState().resetDrafts()
  server.use(
    sessionHandler(VERIFIED_SESSION_FIXTURE),
    dcfBaselineHandler(),
    dcfComputeHandler(),
    scenariosListHandler([]),
  )
}
