import { describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'

import {
  API_BASE,
  loginHandler,
  problemHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { SignInScreen } from '@/screens/signIn/SignInScreen'
import { useDraftStore } from '@/store/draftStore'

/**
 * SCR-008 — Sign In form (L3, test strategy §5.3).
 *
 * Traces UXR-ACC-008/009/011 and UXR-G-004/018/019/020: e-mail + password with a
 * generic credential error (never which-field), the e-mail preserved on failure,
 * links to password reset and registration, submit disabled while invalid, the
 * trigger locked while pending, and the account language preference applied with
 * a return to preserved in-progress work via `?returnUrl`.
 */

const tr = {
  formAriaLabel: catalogValue('tr', 'signIn.formAriaLabel'),
  emailLabel: catalogValue('tr', 'signIn.emailLabel'),
  emailInvalid: catalogValue('tr', 'signIn.emailInvalid'),
  passwordLabel: catalogValue('tr', 'signIn.passwordLabel'),
  passwordRequired: catalogValue('tr', 'signIn.passwordRequired'),
  submit: catalogValue('tr', 'signIn.submit'),
  submitting: catalogValue('tr', 'signIn.submitting'),
  invalidCredentials: catalogValue('tr', 'signIn.invalidCredentials'),
  lockedOut: catalogValue('tr', 'signIn.lockedOut'),
  forgotPassword: catalogValue('tr', 'signIn.forgotPassword'),
  registerLink: catalogValue('tr', 'signIn.registerLink'),
}

const VALID_EMAIL = 'ayse@example.test'
const VALID_PASSWORD = 'correct-horse-battery'

function LocationProbe() {
  const location = useLocation()
  return (
    <span data-testid="location">
      {location.pathname}
      {location.search}
    </span>
  )
}

function renderSignIn(entry = '/sign-in') {
  return renderWithProviders(
    <MemoryRouter initialEntries={[entry]}>
      <LocationProbe />
      <Routes>
        <Route path="/sign-in" element={<SignInScreen />} />
        <Route path="/" element={<div data-testid="home" />} />
        <Route path="/stocks" element={<div data-testid="stocks" />} />
      </Routes>
    </MemoryRouter>,
  )
}

async function fill(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(tr.emailLabel, { exact: false }), VALID_EMAIL)
  await user.type(
    screen.getByLabelText(tr.passwordLabel, { exact: false }),
    VALID_PASSWORD,
  )
}

describe('SCR-008 — Sign In form contract', () => {
  it('collects e-mail and password, and links to password reset and registration', () => {
    const { container } = renderSignIn()

    expect(container.querySelectorAll('input')).toHaveLength(2)
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toBeInTheDocument()
    expect(screen.getByLabelText(tr.passwordLabel, { exact: false })).toBeInTheDocument()

    expect(
      screen.getByRole('link', { name: tr.forgotPassword }),
    ).toHaveAttribute('href', '/reset-password')
    expect(
      screen.getByRole('link', { name: tr.registerLink }),
    ).toHaveAttribute('href', '/register')
  })

  it('validates each field inline and keeps submit disabled while invalid', async () => {
    const user = userEvent.setup()
    renderSignIn()

    const submit = screen.getByRole('button', { name: tr.submit })
    expect(submit).toBeDisabled()

    const email = screen.getByLabelText(tr.emailLabel, { exact: false })
    await user.type(email, 'not-an-email')
    await user.tab()
    expect(await screen.findByText(tr.emailInvalid)).toBeInTheDocument()
    expect(email).toHaveAttribute('aria-invalid', 'true')

    await user.clear(email)
    await user.type(email, VALID_EMAIL)
    // Password still empty → submit disabled.
    expect(submit).toBeDisabled()

    await user.type(screen.getByLabelText(tr.passwordLabel, { exact: false }), VALID_PASSWORD)
    expect(submit).toBeEnabled()
  })

  it('locks the trigger and the fields while the sign-in mutation is pending', async () => {
    const user = userEvent.setup()
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.post(`${API_BASE}/auth/login`, async () => {
        await gate
        return HttpResponse.json({ languagePref: 'tr' })
      }),
    )

    renderSignIn()
    await fill(user)

    await user.click(screen.getByRole('button', { name: tr.submit }))

    const pendingSubmit = screen.getByRole('button', { name: tr.submitting })
    expect(pendingSubmit).toBeDisabled()
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toBeDisabled()
    expect(screen.getByLabelText(tr.passwordLabel, { exact: false })).toBeDisabled()

    release()
    await waitFor(() =>
      expect(
        screen.queryByRole('button', { name: tr.submitting }),
      ).not.toBeInTheDocument(),
    )
  })

  it('renders a generic credential error and preserves the e-mail on failure', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/auth/login`, 'INVALID_CREDENTIALS', {
        method: 'post',
      }),
    )

    renderSignIn()
    await fill(user)
    await user.click(screen.getByRole('button', { name: tr.submit }))

    await screen.findByText(tr.invalidCredentials)
    // The error is generic (no which-field signal) and the input is preserved.
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toHaveValue(VALID_EMAIL)
  })

  it('explains a lockout with its own plain-language message', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/auth/login`, 'LOCKED_OUT', { method: 'post' }),
    )

    renderSignIn()
    await fill(user)
    await user.click(screen.getByRole('button', { name: tr.submit }))

    await screen.findByText(tr.lockedOut)
  })

  it('applies the account language preference and returns to preserved work on success (UXR-ACC-009)', async () => {
    const user = userEvent.setup()
    server.use(loginHandler({ languagePref: 'en' }))
    useDraftStore.getState().resetDrafts()

    const { i18n } = renderSignIn('/sign-in?returnUrl=%2Fstocks%3Fsector%3DA')
    await fill(user)
    await user.click(screen.getByRole('button', { name: tr.submit }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/stocks?sector=A'))
    expect(i18n.resolvedLanguage).toBe('en')
    expect(useDraftStore.getState().returnUrl).toBe('/stocks?sector=A')
  })

  it('ignores a cross-origin returnUrl (SF3)', async () => {
    const user = userEvent.setup()
    server.use(loginHandler({ languagePref: 'tr' }))

    renderSignIn('/sign-in?returnUrl=%2F%2Fevil.example')
    await fill(user)
    await user.click(screen.getByRole('button', { name: tr.submit }))

    await waitFor(() => expect(screen.getByTestId('home')).toBeInTheDocument())
    expect(screen.getByTestId('location')).toHaveTextContent('/')
  })
})
