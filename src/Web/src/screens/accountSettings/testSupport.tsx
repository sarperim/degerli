/* eslint-disable react-refresh/only-export-components -- test harness: stub
   route components + render helpers intentionally share one test-only module. */
import { Link, MemoryRouter, Route, Routes, useLocation } from 'react-router'

import { AccountSettingsScreen } from '@/screens/accountSettings/AccountSettingsScreen'
import { SavedScreensScreen } from '@/screens/savedScreens/SavedScreensScreen'
import { server } from '@/test/msw/server'
import {
  ANONYMOUS_SESSION_FIXTURE,
  ME_UNVERIFIED_FIXTURE,
  ME_VERIFIED_FIXTURE,
  VERIFIED_SESSION_FIXTURE,
  deleteAccountHandler,
  meHandler,
  passwordChangeHandler,
  resendVerificationHandler,
  savedScreensListHandler,
  screenerMetricsHandler,
  sessionHandler,
  updateLanguageHandler,
} from '@/test/msw/handlers'
import { renderWithProviders } from '@/test/render'
import type { MeResponse, SessionResponse } from '@/lib/api/contracts'

/**
 * Shared harness for the SCR-010 component tests (TKT-acc-010). A stub home and
 * the real SCR-004 list screen let the delete end-to-end flow reach the
 * saved-screens surface after sign-out (TC-ACC-032).
 */

export { ME_UNVERIFIED_FIXTURE, ME_VERIFIED_FIXTURE }

function LocationProbe() {
  const location = useLocation()
  return (
    <span data-testid="location">
      {location.pathname}
      {location.search}
    </span>
  )
}

function HomeStub() {
  return (
    <div data-testid="home">
      <Link to="/screens">go-screens</Link>
    </div>
  )
}

export function renderAccountSettings(initialEntries: string[] = ['/settings']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <LocationProbe />
      <Routes>
        <Route path="/settings" element={<AccountSettingsScreen />} />
        <Route path="/screens" element={<SavedScreensScreen />} />
        <Route path="/" element={<HomeStub />} />
        <Route path="/sign-in" element={<div data-testid="sign-in-page" />} />
        <Route path="/register" element={<div data-testid="register-page" />} />
      </Routes>
    </MemoryRouter>,
  )
}

/**
 * Reset persisted state and register the default SCR-010 handlers: a verified
 * session + account read, and the account mutations.
 */
export function resetAccountSettingsTest(
  session: SessionResponse = VERIFIED_SESSION_FIXTURE,
  me: MeResponse = ME_VERIFIED_FIXTURE,
) {
  sessionStorage.clear()
  server.use(
    sessionHandler(session),
    meHandler(me),
    updateLanguageHandler(me),
    passwordChangeHandler(),
    deleteAccountHandler(),
    resendVerificationHandler(),
    screenerMetricsHandler(),
    savedScreensListHandler(),
  )
}

/** Expose the anonymous fixture for the anonymous-access test. */
export { ANONYMOUS_SESSION_FIXTURE }

/** Interpolate an i18n template the way i18next would. */
export function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) => String(vars[key] ?? ''))
}
