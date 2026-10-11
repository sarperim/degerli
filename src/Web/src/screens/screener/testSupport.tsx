/* eslint-disable react-refresh/only-export-components -- test harness: stub
   route components + render helpers intentionally share one test-only module. */
import { MemoryRouter, Link, Route, Routes, useParams } from 'react-router'

import { ScreenerScreen } from '@/screens/screener/ScreenerScreen'
import { server } from '@/test/msw/server'
import {
  saveScreenHandler,
  screenerMetricsHandler,
  screenerRunHandler,
} from '@/test/msw/handlers'
import { renderWithProviders } from '@/test/render'
import { useDraftStore } from '@/store/draftStore'

/**
 * Shared harness for the SCR-003 component tests (TKT-scr-006). A stub stock
 * page and auth landings let navigation flows be exercised in-memory; the default
 * MSW handlers return the deterministic catalog, run and save fixtures.
 */

function StockPageStub() {
  const { symbol } = useParams()
  return (
    <div data-testid="stock-page">
      <span data-testid="stock-page-symbol">{symbol}</span>
      <Link to="/screener">back-to-screener</Link>
    </div>
  )
}

function AuthStub({ testId }: { testId: string }) {
  return (
    <div data-testid={testId}>
      <Link to="/screener">back-to-screener</Link>
    </div>
  )
}

export function renderScreener(initialEntries: string[] = ['/screener']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/screener" element={<ScreenerScreen />} />
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
        <Route path="/sign-in" element={<AuthStub testId="sign-in-page" />} />
        <Route path="/register" element={<AuthStub testId="register-page" />} />
        <Route path="/settings" element={<div data-testid="settings-page" />} />
      </Routes>
    </MemoryRouter>,
  )
}

/** Reset the persisted draft store and register the default screener handlers. */
export function resetScreenerTest() {
  sessionStorage.clear()
  useDraftStore.getState().resetDrafts()
  server.use(screenerMetricsHandler(), screenerRunHandler(), saveScreenHandler())
}

/** Interpolate an i18n template the way i18next would. */
export function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) =>
    String(vars[key] ?? ''),
  )
}
