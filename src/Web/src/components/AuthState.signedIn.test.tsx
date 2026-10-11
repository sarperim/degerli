import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import { AuthState } from '@/components/AuthState'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  ANONYMOUS_SESSION_FIXTURE,
  logoutHandler,
  sessionHandler,
  VERIFIED_SESSION_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'

/**
 * Header sign-out control (UXR-ACC-010): the header reflects the authenticated
 * state and offers sign-out; invoking it revokes the session and the header flips
 * back to the anonymous state (session-query invalidation, mutation matrix).
 *
 * The anonymous-state contract is covered by `AuthState.test.tsx`; this file adds
 * the signed-in branch this ticket introduces.
 */

describe('AuthState — signed-in and sign-out (UXR-ACC-010)', () => {
  it('shows the account e-mail and a sign-out control while signed in', async () => {
    server.use(sessionHandler(VERIFIED_SESSION_FIXTURE))

    renderWithProviders(<AuthState />)

    expect(await screen.findByTestId('auth-email')).toHaveTextContent(
      VERIFIED_SESSION_FIXTURE.email,
    )
    expect(
      screen.getByRole('button', { name: catalogValue('tr', 'header.signOut') }),
    ).toBeInTheDocument()
  })

  it('ends the session on sign-out and the header reflects the anonymous state', async () => {
    const user = userEvent.setup()
    let sessionCalls = 0
    server.use(
      http.get(`${API_BASE}/auth/session`, () => {
        sessionCalls += 1
        return HttpResponse.json(
          sessionCalls === 1 ? VERIFIED_SESSION_FIXTURE : ANONYMOUS_SESSION_FIXTURE,
        )
      }),
      logoutHandler(),
    )

    renderWithProviders(<AuthState />)

    await screen.findByTestId('auth-email')
    await user.click(
      screen.getByRole('button', { name: catalogValue('tr', 'header.signOut') }),
    )

    expect(
      await screen.findByText(catalogValue('tr', 'header.anonymous')),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: catalogValue('tr', 'header.signOut') }),
    ).not.toBeInTheDocument()
    expect(sessionCalls).toBeGreaterThan(1)
  })
})
