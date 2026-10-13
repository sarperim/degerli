/* eslint-disable react-refresh/only-export-components -- test harness: stub
   route components + render helpers intentionally share one test-only module. */
import { Link, MemoryRouter, Route, Routes, useParams } from 'react-router'

import { DcfCalculatorScreen } from '@/screens/dcf/DcfCalculatorScreen'
import { DcfScenariosScreen } from '@/screens/dcfScenarios/DcfScenariosScreen'
import type { SupportedLanguage } from '@/i18n'
import { useDraftStore } from '@/store/draftStore'
import {
  dcfBaselineHandler,
  dcfComputeHandler,
  deleteScenarioHandler,
  renameScenarioHandler,
  scenariosListHandler,
  sessionHandler,
  VERIFIED_SESSION_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'

/**
 * Shared harness for the SCR-011 component and flow tests (TKT-val-008).
 *
 * Renders SCR-011 together with the SCR-006 calculator (so the "open a scenario
 * into the calculator loaded with it" flow and the picker-invalidation hand-off
 * are exercised in one router), plus the auth/stock landings. A test-only nav bar
 * provides deterministic in-memory navigation between the two screens.
 */

function StockPageStub() {
  const { symbol } = useParams()
  return <div data-testid="stock-page">{symbol}</div>
}

export function renderScenarios(
  initialEntries: string[] = ['/scenarios'],
  language: SupportedLanguage = 'tr',
) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <nav>
        <Link to="/scenarios" data-testid="go-scenarios">
          go-scenarios
        </Link>
        <Link to="/stocks/ALFA/dcf" data-testid="go-calculator">
          go-calculator
        </Link>
      </nav>
      <Routes>
        <Route path="/scenarios" element={<DcfScenariosScreen />} />
        <Route path="/stocks" element={<div data-testid="stocks-page" />} />
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
        <Route path="/stocks/:symbol/dcf" element={<DcfCalculatorScreen />} />
        <Route path="/sign-in" element={<div data-testid="sign-in-page" />} />
        <Route path="/register" element={<div data-testid="register-page" />} />
      </Routes>
    </MemoryRouter>,
    { language },
  )
}

/** Reset the persisted draft store and register the default SCR-011 handlers. */
export function resetScenariosTest(session = VERIFIED_SESSION_FIXTURE) {
  sessionStorage.clear()
  useDraftStore.getState().resetDrafts()
  server.use(
    sessionHandler(session),
    scenariosListHandler(),
    renameScenarioHandler(),
    deleteScenarioHandler(),
    dcfBaselineHandler(),
    dcfComputeHandler(),
  )
}

/** Interpolate an i18n template the way i18next would. */
export function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) => String(vars[key] ?? ''))
}
