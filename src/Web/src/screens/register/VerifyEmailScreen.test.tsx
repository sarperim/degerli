import { afterEach, describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'

import { useSession } from '@/lib/auth/session'
import { clearPendingVerificationToken } from '@/screens/register/pendingVerificationToken'
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
  afterEach(() => {
    clearPendingVerificationToken()
  })

  it('shows a success notice and continue-to-app link when the e-mail is verified', async () => {
    server.use(sessionHandler(), verifyEmailHandler())

    renderVerify('/verify-email?token=valid-token')

    expect(await screen.findByText(tr.verifiedTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.verifiedBody)).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: catalogValue('tr', 'verifyEmail.continueToApp') }),
    ).toHaveAttribute('href', '/')
  })

  it('explains an expired/invalid link and offers a resend path', async () => {
    server.use(
      sessionHandler(),
      problemHandler(`${API_BASE}/auth/verify-email`, 'TOKEN_EXPIRED', {
        method: 'post',
      }),
    )

    const user = userEvent.setup()
    renderVerify('/verify-email?token=expired-token')

    expect(await screen.findByText(tr.failedTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.failedBody)).toBeInTheDocument()

    server.use(resendVerificationHandler())
    await user.click(screen.getByRole('button', { name: tr.resend }))

    expect(await screen.findByText(tr.resent)).toBeInTheDocument()
  })

  it('routes to sign-in (not a dead-end resend) when the link is opened without a session', async () => {
    server.use(
      sessionHandler(),
      problemHandler(`${API_BASE}/auth/verify-email`, 'UNAUTHENTICATED', {
        method: 'post',
      }),
    )

    renderVerify('/verify-email?token=valid-token')

    expect(
      await screen.findByText(catalogValue('tr', 'verifyEmail.unauthenticatedTitle')),
    ).toBeInTheDocument()
    const signInLink = screen.getByRole('link', {
      name: catalogValue('tr', 'verifyEmail.signIn'),
    })
    expect(signInLink).toHaveAttribute('href', '/sign-in?returnUrl=%2Fverify-email')
    expect(signInLink.getAttribute('href')).not.toContain('valid-token')
    // Resend is session-scoped too; offering it here would dead-end (SF2).
    expect(screen.queryByRole('button', { name: tr.resend })).not.toBeInTheDocument()
  })

  it('auto-completes verification after unauthenticated sign-in return using the in-memory token', async () => {
    const user = userEvent.setup()
    let authenticated = false
    let verifiedToken: string | null = null

    server.use(
      http.get(`${API_BASE}/auth/session`, () =>
        HttpResponse.json(
          authenticated ? UNVERIFIED_SESSION_FIXTURE : { authenticated: false },
        ),
      ),
      http.post(`${API_BASE}/auth/verify-email`, async ({ request }) => {
        const body = (await request.json()) as { token: string }
        if (!authenticated) {
          return HttpResponse.json(
            {
              type: 'https://degerli.test/api/v1/errors/unauthenticated',
              title: 'Unauthenticated',
              status: 401,
              code: 'UNAUTHENTICATED',
              traceId: '00-test-0000000000000000-01',
            },
            { status: 401 },
          )
        }
        verifiedToken = body.token
        return HttpResponse.json({ verified: true })
      }),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={['/verify-email?token=valid-token']}>
        <Routes>
          <Route path="/verify-email" element={<VerifyEmailScreen />} />
          <Route
            path="/sign-in"
            element={
              <a
                href="/verify-email"
                onClick={(event) => {
                  event.preventDefault()
                  authenticated = true
                }}
              >
                complete-sign-in
              </a>
            }
          />
        </Routes>
      </MemoryRouter>,
      { queryClient: createTestQueryClient() },
    )

    const signInLink = await screen.findByRole('link', {
      name: catalogValue('tr', 'verifyEmail.signIn'),
    })
    expect(signInLink).toHaveAttribute('href', '/sign-in?returnUrl=%2Fverify-email')

    // Simulate clicking Sign in → completing sign-in → navigating back to /verify-email without token in URL.
    await user.click(signInLink)
    authenticated = true

    renderVerify('/verify-email')
    expect(await screen.findByText(tr.verifiedTitle)).toBeInTheDocument()
    expect(verifiedToken).toBe('valid-token')
  })

  it('shows rate-limited copy with a retry button that succeeds on retry', async () => {
    const user = userEvent.setup()
    let attempts = 0
    server.use(
      sessionHandler(UNVERIFIED_SESSION_FIXTURE),
      http.post(`${API_BASE}/auth/verify-email`, () => {
        attempts += 1
        if (attempts === 1) {
          return HttpResponse.json(
            {
              type: 'https://degerli.test/api/v1/errors/rate-limited',
              title: 'Rate limited',
              status: 429,
              code: 'RATE_LIMITED',
              traceId: '00-test-0000000000000000-01',
            },
            { status: 429 },
          )
        }
        return HttpResponse.json({ verified: true })
      }),
    )

    renderVerify('/verify-email?token=valid-token')

    expect(
      await screen.findByText(catalogValue('tr', 'verifyEmail.rateLimitedTitle')),
    ).toBeInTheDocument()
    expect(
      screen.getByText(catalogValue('tr', 'verifyEmail.rateLimitedBody')),
    ).toBeInTheDocument()
    expect(screen.queryByText(tr.failedTitle)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: tr.resend })).not.toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: catalogValue('tr', 'verifyEmail.retry') }),
    )
    expect(await screen.findByText(tr.verifiedTitle)).toBeInTheDocument()
    expect(attempts).toBe(2)
  })

  it('renders verified state when navigating to /verify-email without a token if session is already verified', async () => {
    let verifyCalls = 0
    server.use(
      sessionHandler(VERIFIED_SESSION_FIXTURE),
      http.post(`${API_BASE}/auth/verify-email`, () => {
        verifyCalls += 1
        return HttpResponse.json({ verified: true })
      }),
    )

    renderVerify('/verify-email')

    expect(await screen.findByText(tr.verifiedTitle)).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: catalogValue('tr', 'verifyEmail.continueToApp') }),
    ).toHaveAttribute('href', '/')
    expect(verifyCalls).toBe(0)
  })

  it('explains a missing token without calling the verify endpoint', async () => {
    let verifyCalls = 0
    server.use(
      sessionHandler(),
      http.post(`${API_BASE}/auth/verify-email`, () => {
        verifyCalls += 1
        return HttpResponse.json({ verified: true })
      }),
    )

    renderVerify('/verify-email')

    expect(await screen.findByText(tr.failedTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.failedBody)).toBeInTheDocument()
    expect(verifyCalls).toBe(0)
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

    renderVerify('/verify-email?token=valid-token')

    // The session starts unverified, then the landing's verify mutation
    // invalidates ['session'] and the probe refetches the verified state.
    await waitFor(() =>
      expect(screen.getByTestId('session-verified')).toHaveTextContent('true'),
    )
    expect(sessionCalls).toBeGreaterThan(1)
  })
})
