/* eslint-disable react-refresh/only-export-components -- test harness: stub
   route components + render helpers intentionally share one test-only module. */
import { MemoryRouter, Route, Routes } from 'react-router'

import { AdminScreen } from '@/screens/admin/AdminScreen'
import { resetAdminUiState } from '@/screens/admin/adminUiStore'
import { server } from '@/test/msw/server'
import {
  adminCoverageHandler,
  adminStatsHandler,
  adminSummaryHandler,
  BUILDER_SESSION_FIXTURE,
  ingestRunsHandlers,
  ingestTriggerHandler,
  quarantineHandlers,
  sessionHandler,
} from '@/test/msw/handlers'
import { renderWithProviders } from '@/test/render'

/**
 * Shared harness for the SCR-012 component tests (TKT-mdf-012).
 *
 * A stub home route lets the access-denied path back to platform content be
 * exercised in-memory; the default handlers return the deterministic admin
 * fixtures (builder session, summary, coverage, ledger, stats).
 */

function HomeStub() {
  return <div data-testid="home" />
}

export function renderAdmin(initialEntries: string[] = ['/admin']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/admin" element={<AdminScreen />} />
        <Route path="/" element={<HomeStub />} />
      </Routes>
    </MemoryRouter>,
  )
}

/** Reset the persisted admin UI state and register the default admin handlers. */
export function resetAdminTest() {
  sessionStorage.clear()
  resetAdminUiState()
  server.use(
    sessionHandler(BUILDER_SESSION_FIXTURE),
    adminSummaryHandler(),
    adminStatsHandler(),
    adminCoverageHandler(),
    ...quarantineHandlers().handlers,
    ...ingestRunsHandlers().handlers,
    ingestTriggerHandler(),
  )
}
