/* eslint-disable react-refresh/only-export-components -- test harness: stub
   route components + render helpers intentionally share one test-only module. */
import { Link, MemoryRouter, Route, Routes, useParams } from 'react-router'

import { SavedScreensScreen } from '@/screens/savedScreens/SavedScreensScreen'
import { ScreenerScreen } from '@/screens/screener/ScreenerScreen'
import { server } from '@/test/msw/server'
import {
  ANONYMOUS_SESSION_FIXTURE,
  deleteScreenHandler,
  renameScreenHandler,
  savedScreensListHandler,
  screenerMetricsHandler,
  screenerRunHandler,
  sessionHandler,
  VERIFIED_SESSION_FIXTURE,
} from '@/test/msw/handlers'
import { renderWithProviders } from '@/test/render'
import { useDraftStore } from '@/store/draftStore'

/**
 * Shared harness for the SCR-004 component tests (TKT-scr-007). A stub stock
 * page and auth landings let the list-screen flows reach the screener context
 * in-memory; the default MSW handlers return the deterministic saved-screen
 * fixtures and the screener catalog/run.
 */

function StockPageStub() {
  const { symbol } = useParams()
  return <div data-testid="stock-page">{symbol}</div>
}

function AuthLandingStub({ testId }: { testId: string }) {
  return (
    <div data-testid={testId}>
      <Link to="/screens">back-to-screens</Link>
    </div>
  )
}

export function renderSavedScreens(initialEntries: string[] = ['/screens']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/screens" element={<SavedScreensScreen />} />
        <Route path="/screener" element={<ScreenerScreen />} />
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
        <Route path="/sign-in" element={<AuthLandingStub testId="sign-in-page" />} />
        <Route path="/register" element={<AuthLandingStub testId="register-page" />} />
      </Routes>
    </MemoryRouter>,
  )
}

/** Reset the persisted draft store and register the default SCR-004 handlers. */
export function resetSavedScreensTest(session = VERIFIED_SESSION_FIXTURE) {
  sessionStorage.clear()
  useDraftStore.getState().resetDrafts()
  server.use(
    sessionHandler(session),
    savedScreensListHandler(),
    screenerMetricsHandler(),
    screenerRunHandler(),
    renameScreenHandler({ ...SAVED_SCREEN_RENAME_FIXTURE }),
    deleteScreenHandler(),
  )
}

/** The renamed-screen response for screen id 1 (TC-SCR-030 rename branch). */
export const SAVED_SCREEN_RENAME_FIXTURE = {
  id: 1,
  name: 'Yeni ad',
  criteria: [
    { metricCode: 'pe', bound: 'max' as const, maxValue: 15 },
    { metricCode: 'roe', bound: 'min' as const, minValue: 0.15 },
  ],
  createdAt: '2026-10-05T20:00:00Z',
  updatedAt: '2026-10-07T20:00:00Z',
}

/** Expose the anonymous fixture for the anonymous-access test. */
export { ANONYMOUS_SESSION_FIXTURE }

/** Interpolate an i18n template the way i18next would. */
export function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) => String(vars[key] ?? ''))
}
