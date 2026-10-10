import { describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'

import { API_BASE, problemHandler, registerHandler } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { RegisterScreen } from '@/screens/register/RegisterScreen'
import { useDraftStore } from '@/store/draftStore'

/**
 * TC-ACC-022 — Registration form contract (L3, test strategy §5.3).
 *
 * Traces UXR-ACC-001/002/003, UXR-G-018/019/020. Decision table: empty / invalid
 * / valid × consent states. The form collects exactly e-mail, password and
 * consent; rules are visible; validation is inline; submit is disabled while
 * invalid; the trigger locks while pending; the bilingual notice is reviewable
 * before consent; input is preserved on failure; focus is managed.
 */

const tr = {
  formAriaLabel: catalogValue('tr', 'register.formAriaLabel'),
  emailLabel: catalogValue('tr', 'register.emailLabel'),
  passwordLabel: catalogValue('tr', 'register.passwordLabel'),
  passwordRules: catalogValue('tr', 'register.passwordRules'),
  emailInvalid: catalogValue('tr', 'register.emailInvalid'),
  passwordInvalid: catalogValue('tr', 'register.passwordInvalid').replace(
    '{{min}}',
    '10',
  ),
  consentLabel: catalogValue('tr', 'register.consentLabel'),
  privacyNoticeTr: catalogValue('tr', 'register.privacyNoticeTr'),
  privacyNoticeEn: catalogValue('tr', 'register.privacyNoticeEn'),
  submit: catalogValue('tr', 'register.submit'),
  submitting: catalogValue('tr', 'register.submitting'),
  emailTaken: catalogValue('tr', 'register.emailTaken'),
}

const VALID_EMAIL = 'ayse@example.test'
const VALID_PASSWORD = 'correct-horse-battery'

function renderRegister(entry = '/register') {
  return renderWithProviders(
    <MemoryRouter initialEntries={[entry]}>
      <RegisterScreen />
    </MemoryRouter>,
  )
}

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(tr.emailLabel, { exact: false }), VALID_EMAIL)
  await user.type(screen.getByLabelText(tr.passwordLabel, { exact: false }), VALID_PASSWORD)
  await user.click(screen.getByRole('checkbox', { name: tr.consentLabel }))
}

describe('TC-ACC-022 — Registration form contract', () => {
  it('collects exactly three inputs (e-mail, password, consent) — no other fields', () => {
    const { container } = renderRegister()

    expect(container.querySelectorAll('input')).toHaveLength(3)
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toBeInTheDocument()
    expect(screen.getByLabelText(tr.passwordLabel, { exact: false })).toBeInTheDocument()
    expect(
      screen.getByRole('checkbox', { name: tr.consentLabel }),
    ).toBeInTheDocument()
  })

  it('shows the password rules at or before the first invalid submission', () => {
    renderRegister()
    expect(screen.getByText(tr.passwordRules)).toBeInTheDocument()
  })

  it('validates each field inline with plain-language errors', async () => {
    const user = userEvent.setup()
    renderRegister()

    const email = screen.getByLabelText(tr.emailLabel, { exact: false })
    await user.type(email, 'not-an-email')
    await user.tab()
    expect(await screen.findByText(tr.emailInvalid)).toBeInTheDocument()
    expect(email).toHaveAttribute('aria-invalid', 'true')

    const password = screen.getByLabelText(tr.passwordLabel, { exact: false })
    await user.type(password, 'short')
    await user.tab()
    expect(await screen.findByText(tr.passwordInvalid)).toBeInTheDocument()
    expect(password).toHaveAttribute('aria-invalid', 'true')
  })

  it('keeps submit disabled while any required input is missing or invalid', async () => {
    const user = userEvent.setup()
    renderRegister()

    const submit = screen.getByRole('button', { name: tr.submit })
    expect(submit).toBeDisabled()

    await user.type(screen.getByLabelText(tr.emailLabel, { exact: false }), VALID_EMAIL)
    await user.type(screen.getByLabelText(tr.passwordLabel, { exact: false }), VALID_PASSWORD)
    // Still missing consent.
    expect(submit).toBeDisabled()

    await user.click(screen.getByRole('checkbox', { name: tr.consentLabel }))
    expect(submit).toBeEnabled()
  })

  it('locks the trigger while the registration mutation is pending', async () => {
    const user = userEvent.setup()
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.post(`${API_BASE}/auth/register`, async () => {
        await gate
        return HttpResponse.json(
          {
            authenticated: true,
            email: VALID_EMAIL,
            verified: false,
            languagePref: 'tr',
            role: 'user',
          },
          { status: 201 },
        )
      }),
    )

    renderRegister()
    await fillValid(user)

    const submit = screen.getByRole('button', { name: tr.submit })
    await user.click(submit)

    // Pending: trigger reflects the lock, is disabled, and cannot be re-invoked.
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

  it('shows the bilingual privacy notice reviewable before consent is given', () => {
    renderRegister()

    expect(screen.getByText(tr.privacyNoticeTr)).toBeInTheDocument()
    expect(screen.getByText(tr.privacyNoticeEn)).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: tr.consentLabel })).not.toBeChecked()
  })

  it('preserves all input when registration fails', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/auth/register`, 'EMAIL_TAKEN', {
        method: 'post',
      }),
    )

    renderRegister()
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: tr.submit }))

    await screen.findByText(tr.emailTaken)
    expect(screen.getByLabelText(tr.emailLabel, { exact: false })).toHaveValue(VALID_EMAIL)
    expect(screen.getByLabelText(tr.passwordLabel, { exact: false })).toHaveValue(VALID_PASSWORD)
    expect(
      screen.getByRole('checkbox', { name: tr.consentLabel }),
    ).toBeChecked()
  })

  it('places focus on the first field on load and on the first invalid field on failed submission', async () => {
    const user = userEvent.setup()
    renderRegister()

    const email = screen.getByLabelText(tr.emailLabel, { exact: false })
    expect(email).toHaveFocus()

    await user.type(email, 'not-an-email')
    // A disabled submit cannot be clicked; simulate the failed submission path.
    fireEvent.submit(screen.getByRole('form', { name: tr.formAriaLabel }))

    await waitFor(() => expect(email).toHaveFocus())
  })
})

describe('TC-ACC-023 — Register error and outcome states render', () => {
  it('renders EMAIL_TAKEN with a path to sign in and preserves input', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/auth/register`, 'EMAIL_TAKEN', {
        method: 'post',
      }),
    )
    useDraftStore.getState().resetDrafts()

    renderRegister()
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: tr.submit }))

    await screen.findByText(tr.emailTaken)
    const signIn = screen.getByRole('link', {
      name: catalogValue('tr', 'register.signInInstead'),
    })
    expect(signIn).toHaveAttribute('href', '/sign-in')
  })

  it('renders signed-in confirmation, the verification notice, and return to preserved work on success', async () => {
    const user = userEvent.setup()
    server.use(registerHandler())

    renderRegister('/register?returnUrl=%2Fscreener')
    await fillValid(user)
    await user.click(screen.getByRole('button', { name: tr.submit }))

    expect(
      await screen.findByText(catalogValue('tr', 'register.successTitle')),
    ).toBeInTheDocument()

    const notice = await screen.findByTestId('registration-verification-notice')
    const body = catalogValue('tr', 'register.verificationSentBody')
    const [beforeEmail, afterEmail] = body.split('{{email}}')
    if (beforeEmail) expect(notice).toHaveTextContent(beforeEmail)
    if (afterEmail) expect(notice).toHaveTextContent(afterEmail)
    expect(notice).toHaveTextContent(VALID_EMAIL)

    const returnLink = screen.getByRole('link', {
      name: catalogValue('tr', 'register.returnToWork'),
    })
    expect(returnLink).toHaveAttribute('href', '/screener')
  })
})
