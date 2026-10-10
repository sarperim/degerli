import { describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'

import { useSession } from '@/lib/auth/session'
import { VerifyEmailScreen } from '@/screens/register/VerifyEmailScreen'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  problemHandler,
  resendVerificationHandler,
  sessionHandler,
  UNVERIFIED_SESSION_FIXTURE,
  VERIFIED_SESSION_FIXTURE,
  verifyEmailHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'
import { createTestQueryClient } from '@/test/query/testQueryClient'

/**
 * TC-ACC-024 — Verification landing states (L3, test strategy §5.3).
 *
 * Traces UXR-ACC-006, SCR-007 §3. Equivalence classes: verified / expired. The
 * landing shows a success notice when verified and a plain-language explanation
 * with a resend path when expired/invalid; on success the open session's query is
 * invalidated so its blocked-save state clears (M-9).
 */

const tr = {
  verifiedTitle: catalogValue('tr', 'verifyEmail.verifiedTitle'),
  verifiedBody: catalogValue('tr', 'verifyEmail.verifiedBody'),
  failedTitle: catalogValue('tr', 'verifyEmail.failedTitle'),
  failedBody: catalogValue('tr', 'verifyEmail.failedBody'),
  resend: catalogValue('tr', 'verifyEmail.resend'),
  resent: catalogValue('tr', 'verifyEmail.resent'),
}

/** Reads the shared session query so invalidation is observable (M-9). */
function SessionProbe() {
  const { data } = useSession()
  const verified = data && data.authenticated ? String(data.verified) : 'none'
  return <span data-testid="session-verified">{verified}</span>
}

function renderVerify(entry: string, queryClient = createTestQueryClient()) {
  return renderWithProviders(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProbe />
      <VerifyEmailScreen />
    </MemoryRouter>,
    { queryClient },
  )
}

describe('TC-ACC-024 — Verification landing states', () => {
  it('shows a success notice when the e-mail is verified', async () => {
    server.use(sessionHandler(), verifyEmailHandler())

    renderVerify('/register/verify?token=valid-token')

    expect(await screen.findByText(tr.verifiedTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.verifiedBody)).toBeInTheDocument()
  })

  it('explains an expired/invalid link and offers a resend path', async () => {
    server.use(
      sessionHandler(),
      problemHandler(`${API_BASE}/auth/verify-email`, 'TOKEN_EXPIRED', {
        method: 'post',
      }),
    )

    const user = userEvent.setup()
    renderVerify('/register/verify?token=expired-token')

    expect(await screen.findByText(tr.failedTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.failedBody)).toBeInTheDocument()

    server.use(resendVerificationHandler())
    await user.click(screen.getByRole('button', { name: tr.resend }))

    expect(await screen.findByText(tr.resent)).toBeInTheDocument()
  })

  it("clears the open session's blocked state via session-query invalidation (M-9)", async () => {
    let sessionCalls = 0
    server.use(
      http.get(`${API_BASE}/auth/session`, () => {
        sessionCalls += 1
        return HttpResponse.json(
          sessionCalls === 1 ? UNVERIFIED_SESSION_FIXTURE : VERIFIED_SESSION_FIXTURE,
        )
      }),
      verifyEmailHandler(),
    )

    renderVerify('/register/verify?token=valid-token')

    // The session starts unverified, then the landing's verify mutation
    // invalidates ['session'] and the probe refetches the verified state.
    await waitFor(() =>
      expect(screen.getByTestId('session-verified')).toHaveTextContent('true'),
    )
    expect(sessionCalls).toBeGreaterThan(1)
  })
})
