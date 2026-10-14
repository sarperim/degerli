import { describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'

import { useSession } from '@/lib/auth/session'
import { PasswordResetScreen } from '@/screens/password-reset/PasswordResetScreen'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  ANONYMOUS_SESSION_FIXTURE,
  forgotPasswordHandler,
  problemHandler,
  resetPasswordHandler,
  VERIFIED_SESSION_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'
import { createTestQueryClient } from '@/test/query/testQueryClient'

/**
 * TC-ACC-030 — Password reset flow, UI states (L3 with MSW; test strategy §5.3).
 *
 * Traces UXR-ACC-012..015, SCR-009 flows 1–2. Decision/state coverage: request
 * (empty/invalid/valid), the enumeration-neutral confirmation (identical copy for
 * a registered and an unknown address), the set-password stage reached directly
 * from the e-mailed link, inline policy validation, the expired/invalid-link
 * explanation with its re-request path, and success → signed-in state.
 */

const tr = {
  requestFormAriaLabel: catalogValue('tr', 'passwordReset.requestFormAriaLabel'),
  emailLabel: catalogValue('tr', 'passwordReset.emailLabel'),
  emailInvalid: catalogValue('tr', 'passwordReset.emailInvalid'),
  requestSubmit: catalogValue('tr', 'passwordReset.requestSubmit'),
  requestSubmitting: catalogValue('tr', 'passwordReset.requestSubmitting'),
  requestGenericError: catalogValue('tr', 'passwordReset.requestGenericError'),
  neutralTitle: catalogValue('tr', 'passwordReset.neutralTitle'),
  neutralBody: catalogValue('tr', 'passwordReset.neutralBody'),
  setFormAriaLabel: catalogValue('tr', 'passwordReset.setFormAriaLabel'),
  passwordLabel: catalogValue('tr', 'passwordReset.passwordLabel'),
  passwordRules: catalogValue('tr', 'passwordReset.passwordRules'),
  passwordInvalid: catalogValue('tr', 'passwordReset.passwordInvalid').replace(
    '{{min}}',
    '10',
  ),
  setSubmit: catalogValue('tr', 'passwordReset.setSubmit'),
  setSubmitting: catalogValue('tr', 'passwordReset.setSubmitting'),
  expiredTitle: catalogValue('tr', 'passwordReset.expiredTitle'),
  expiredBody: catalogValue('tr', 'passwordReset.expiredBody'),
  requestNewLink: catalogValue('tr', 'passwordReset.requestNewLink'),
  successTitle: catalogValue('tr', 'passwordReset.successTitle'),
  continueToApp: catalogValue('tr', 'passwordReset.continueToApp'),
}

const VALID_EMAIL = 'ayse@example.test'
const VALID_PASSWORD = 'correct-horse-battery'

function renderReset(entry = '/reset-password', queryClient = createTestQueryClient()) {
  return renderWithProviders(
    <MemoryRouter initialEntries={[entry]}>
      <PasswordResetScreen />
    </MemoryRouter>,
    { queryClient },
  )
}

describe('TC-ACC-030 — request stage (neutral confirmation)', () => {
  it('collects an e-mail and keeps submit disabled until it is valid', async () => {
    const user = userEvent.setup()
    renderReset()

    expect(screen.getAllByRole('textbox')).toHaveLength(1)
    const email = screen.getByLabelText(tr.emailLabel, { exact: false })
    const submit = screen.getByRole('button', { name: tr.requestSubmit })
    expect(submit).toBeDisabled()

    await user.type(email, 'not-an-email')
    await user.tab()
    expect(await screen.findByText(tr.emailInvalid)).toBeInTheDocument()
    expect(submit).toBeDisabled()

    await user.clear(email)
    await user.type(email, VALID_EMAIL)
    expect(submit).toBeEnabled()
  })

  it('shows identical neutral confirmation copy for a registered address (no enumeration signal)', async () => {
    const user = userEvent.setup()
    let sentEmail: string | undefined
    server.use(
      http.post(`${API_BASE}/auth/forgot-password`, async ({ request }) => {
        sentEmail = ((await request.json()) as { email: string }).email
        return HttpResponse.json({ status: 'accepted' })
      }),
    )

    renderReset()
    await user.type(screen.getByLabelText(tr.emailLabel, { exact: false }), VALID_EMAIL)
    await user.click(screen.getByRole('button', { name: tr.requestSubmit }))

    expect(await screen.findByText(tr.neutralTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.neutralBody)).toBeInTheDocument()
    expect(sentEmail).toBe(VALID_EMAIL)
  })

  it('shows the same neutral confirmation copy for an unknown address', async () => {
    const user = userEvent.setup()
    const unknown = 'nobody@example.test'
    server.use(forgotPasswordHandler())

    renderReset()
    await user.type(screen.getByLabelText(tr.emailLabel, { exact: false }), unknown)
    await user.click(screen.getByRole('button', { name: tr.requestSubmit }))

    expect(await screen.findByText(tr.neutralTitle)).toBeInTheDocument()
    // Fixed copy — the response body is never rendered, and the e-mail is not echoed.
    expect(screen.getByText(tr.neutralBody)).toBeInTheDocument()
    expect(screen.queryByText(unknown)).not.toBeInTheDocument()
  })

  it('shows a plain-language error and preserves the e-mail when dispatch fails', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/auth/forgot-password`, 'INTERNAL', {
        method: 'post',
      }),
    )

    renderReset()
    await user.type(screen.getByLabelText(tr.emailLabel, { exact: false }), VALID_EMAIL)
    await user.click(screen.getByRole('button', { name: tr.requestSubmit }))

    expect(await screen.findByText(tr.requestGenericError)).toBeInTheDocument()
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toHaveValue(
      VALID_EMAIL,
    )
  })

  it('locks the trigger while the request mutation is pending', async () => {
    const user = userEvent.setup()
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.post(`${API_BASE}/auth/forgot-password`, async () => {
        await gate
        return HttpResponse.json({ status: 'accepted' })
      }),
    )

    renderReset()
    await user.type(screen.getByLabelText(tr.emailLabel, { exact: false }), VALID_EMAIL)
    await user.click(screen.getByRole('button', { name: tr.requestSubmit }))

    expect(screen.getByRole('button', { name: tr.requestSubmitting })).toBeDisabled()
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toBeDisabled()

    release()
    await waitFor(() =>
      expect(
        screen.queryByRole('button', { name: tr.requestSubmitting }),
      ).not.toBeInTheDocument(),
    )
  })

  it('places focus on the e-mail field on load', () => {
    renderReset()
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toHaveFocus()
  })
})

describe('TC-ACC-030 — set-password stage (e-mailed link)', () => {
  it('is directly reachable from the e-mailed link and shows the rules', () => {
    renderReset('/reset-password?token=valid-token')

    expect(screen.getByLabelText(tr.passwordLabel, { exact: false })).toBeInTheDocument()
    expect(screen.getByText(tr.passwordRules)).toBeInTheDocument()
    expect(screen.getByLabelText(tr.passwordLabel, { exact: false })).toHaveFocus()
  })

  it('enforces the password policy inline and keeps submit disabled while invalid', async () => {
    const user = userEvent.setup()
    renderReset('/reset-password?token=valid-token')

    const password = screen.getByLabelText(tr.passwordLabel, { exact: false })
    const submit = screen.getByRole('button', { name: tr.setSubmit })
    expect(submit).toBeDisabled()

    await user.type(password, 'short123') // 8 chars
    await user.tab()
    expect(await screen.findByText(tr.passwordInvalid)).toBeInTheDocument()
    expect(password).toHaveAttribute('aria-invalid', 'true')
    expect(submit).toBeDisabled()

    await user.type(password, '45') // 10 chars total
    expect(submit).toBeEnabled()
  })

  it('explains an expired/invalid link and offers a re-request path', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/auth/reset-password`, 'TOKEN_EXPIRED', {
        method: 'post',
      }),
    )

    renderReset('/reset-password?token=expired-token')
    await user.type(
      screen.getByLabelText(tr.passwordLabel, { exact: false }),
      VALID_PASSWORD,
    )
    await user.click(screen.getByRole('button', { name: tr.setSubmit }))

    expect(await screen.findByText(tr.expiredTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.expiredBody)).toBeInTheDocument()

    const reRequest = screen.getByRole('link', { name: tr.requestNewLink })
    expect(reRequest).toHaveAttribute('href', '/reset-password')
    // The form is replaced by the explanation.
    expect(
      screen.queryByLabelText(tr.passwordLabel, { exact: false }),
    ).not.toBeInTheDocument()
  })

  it('signs the user in on success and invalidates the session query', async () => {
    const user = userEvent.setup()
    let sessionCalls = 0
    server.use(
      http.get(`${API_BASE}/auth/session`, () => {
        sessionCalls += 1
        return HttpResponse.json(
          sessionCalls === 1 ? ANONYMOUS_SESSION_FIXTURE : VERIFIED_SESSION_FIXTURE,
        )
      }),
      resetPasswordHandler(),
    )

    function SessionProbe() {
      const { data } = useSession()
      const state = data && data.authenticated ? data.email : 'anonymous'
      return <span data-testid="session-state">{state}</span>
    }

    renderWithProviders(
      <MemoryRouter initialEntries={['/reset-password?token=valid-token']}>
        <SessionProbe />
        <PasswordResetScreen />
      </MemoryRouter>,
      { queryClient: createTestQueryClient() },
    )

    await user.type(
      screen.getByLabelText(tr.passwordLabel, { exact: false }),
      VALID_PASSWORD,
    )
    await user.click(screen.getByRole('button', { name: tr.setSubmit }))

    expect(await screen.findByText(tr.successTitle)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: tr.continueToApp })).toHaveAttribute(
      'href',
      '/',
    )
    await waitFor(() =>
      expect(screen.getByTestId('session-state')).toHaveTextContent(
        VERIFIED_SESSION_FIXTURE.email,
      ),
    )
    expect(sessionCalls).toBeGreaterThan(1)
  })

  it('locks the trigger while the reset mutation is pending', async () => {
    const user = userEvent.setup()
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.post(`${API_BASE}/auth/reset-password`, async () => {
        await gate
        return HttpResponse.json({ status: 'reset' })
      }),
    )

    renderReset('/reset-password?token=valid-token')
    await user.type(
      screen.getByLabelText(tr.passwordLabel, { exact: false }),
      VALID_PASSWORD,
    )
    await user.click(screen.getByRole('button', { name: tr.setSubmit }))

    expect(screen.getByRole('button', { name: tr.setSubmitting })).toBeDisabled()
    expect(screen.getByLabelText(tr.passwordLabel, { exact: false })).toBeDisabled()

    release()
    await waitFor(() =>
      expect(
        screen.queryByRole('button', { name: tr.setSubmitting }),
      ).not.toBeInTheDocument(),
    )
  })
})
