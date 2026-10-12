import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'

import type { SavedScreen } from '@/lib/api/contracts'
import {
  API_BASE,
  SAVED_SCREENS_FIXTURE,
  screenerMetricsHandler,
  sessionHandler,
  VERIFIED_SESSION_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderSavedScreens } from '@/screens/savedScreens/testSupport'
import { useDraftStore } from '@/store/draftStore'

/**
 * TC-SCR-031 — Server-side persistence across devices (L4).
 *
 * Traces NFR-SCR-005, SC-003. A screen saved in browser context A appears in an
 * independent context B after sign-in: the list is server truth (Postgres), never
 * device state. The two contexts here share a mutable "server" list and each
 * holds its own sessionStorage and query cache — context B must fetch the server
 * to see what context A saved.
 */
describe('TC-SCR-031 — server-side persistence across devices', () => {
  it('shows a screen saved in an independent context after sign-in', async () => {
    const serverScreens: SavedScreen[] = [SAVED_SCREENS_FIXTURE[0]]
    let getCalls = 0
    server.use(
      sessionHandler(VERIFIED_SESSION_FIXTURE),
      http.get(`${API_BASE}/me/screens`, () => {
        getCalls += 1
        return HttpResponse.json({ screens: serverScreens })
      }),
      screenerMetricsHandler(),
    )

    sessionStorage.clear()
    useDraftStore.getState().resetDrafts()

    // Context A — an independent device signs in and reads the list from the server.
    const contextA = renderSavedScreens()
    await screen.findByTestId('saved-screen-row-1')
    expect(getCalls).toBe(1)
    contextA.unmount()

    // Context A saves a new screen — it lands in server truth only.
    serverScreens.push(SAVED_SCREENS_FIXTURE[1])

    // Context B — a *different* device: fresh sessionStorage and a fresh cache.
    sessionStorage.clear()
    useDraftStore.getState().resetDrafts()
    renderSavedScreens()

    // The screen saved in context A appears in context B after sign-in, because
    // the list is fetched from the server, not read from device state.
    await screen.findByTestId('saved-screen-row-2')
    expect(getCalls).toBe(2)
  })
})
