import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import { catalogValue } from '@/test/i18n'
import {
  ANONYMOUS_SESSION_FIXTURE,
  API_BASE,
  UNVERIFIED_SESSION_FIXTURE,
  problemHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import {
  ME_UNVERIFIED_FIXTURE,
  ME_VERIFIED_FIXTURE,
  fill,
  renderAccountSettings,
  resetAccountSettingsTest,
} from '@/screens/accountSettings/testSupport'

/**
 * SCR-010 — Account Settings component contract (test strategy §5.3, L3).
 *
 * TC-ACC-025 — the screen contains exactly the required sections (language
 * preference, password change, entry points, deletion, verification status +
 * resend while unverified) and nothing else; anonymous direct access presents
 * the sign-in path (UXR-ACC-016, UXR-ACC-020).
 * TC-ACC-026 — account deletion sits behind a focus-trapping confirmation that
 * states saved screens and DCF scenarios will be lost; cancel does nothing
 * (UXR-ACC-019, UXR-G-022).
 *
 * The two pinned L4 cases delivered by this ticket — TC-ACC-029 (language
 * preference across devices) and TC-ACC-032 (delete account end-to-end) — live
 * in `tests/e2e/src/account-settings.spec.ts` against the full compose stack,
 * where they exercise real cross-context behaviour (test strategy §5.4).
 * Password change (UXR-ACC-018) is covered behaviourally below.
 */

const TR = (key: string) => catalogValue('tr', key)

const SECTION_IDS = [
  'settings-language',
  'settings-password',
  'settings-entry-points',
  'settings-deletion',
  'settings-verification',
]

beforeEach(() => resetAccountSettingsTest())

describe('TC-ACC-025 — Account Settings contains exactly the required sections', () => {
  it('renders the required sections and nothing else', async () => {
    const { container } = renderAccountSettings()

    await screen.findByTestId('settings-language')
    for (const id of SECTION_IDS) {
      expect(screen.getByTestId(id)).toBeInTheDocument()
    }
    // Exactly the five documented sections — no more (UXR-ACC-016).
    expect(container.querySelectorAll('[data-settings-section]')).toHaveLength(5)
    // Minimal posture (BR-ACC-001): the only inputs are the password-change pair.
    expect(container.querySelectorAll('input')).toHaveLength(2)
  })

  it('offers entry points to My Saved Screens and My DCF Scenarios', async () => {
    renderAccountSettings()

    const section = await screen.findByTestId('settings-entry-points')
    expect(
      within(section).getByRole('link', { name: TR('accountSettings.entryScreens') }),
    ).toHaveAttribute('href', '/screens')
    expect(
      within(section).getByRole('link', { name: TR('accountSettings.entryScenarios') }),
    ).toHaveAttribute('href', '/scenarios')
  })

  it('shows the verification status and offers no resend when verified', async () => {
    renderAccountSettings()

    const section = await screen.findByTestId('settings-verification')
    expect(section).toHaveTextContent(TR('accountSettings.verifiedBadge'))
    expect(section).toHaveTextContent(ME_VERIFIED_FIXTURE.email)
    expect(
      within(section).queryByRole('button', { name: TR('accountSettings.resend') }),
    ).not.toBeInTheDocument()
  })

  it('offers the resend action while unverified (UXR-ACC-021)', async () => {
    const user = userEvent.setup()
    resetAccountSettingsTest(UNVERIFIED_SESSION_FIXTURE, ME_UNVERIFIED_FIXTURE)

    renderAccountSettings()

    const section = await screen.findByTestId('settings-verification')
    expect(section).toHaveTextContent(TR('accountSettings.unverifiedBadge'))

    await user.click(
      within(section).getByRole('button', { name: TR('accountSettings.resend') }),
    )

    expect(await screen.findByTestId('resend-sent')).toBeInTheDocument()
  })

  it('presents the sign-in path on anonymous direct access (UXR-ACC-020)', async () => {
    resetAccountSettingsTest(ANONYMOUS_SESSION_FIXTURE)

    const { container } = renderAccountSettings()

    const state = await screen.findByTestId('settings-anonymous')
    expect(
      within(state).getByRole('link', { name: TR('accountSettings.signIn') }),
    ).toHaveAttribute('href', '/sign-in?returnUrl=%2Fsettings')
    // No settings sections render while anonymous.
    expect(container.querySelectorAll('[data-settings-section]')).toHaveLength(0)
  })
})

describe('TC-ACC-026 — deletion double-confirmation', () => {
  it('requires a confirmation stating that saved screens and DCF scenarios will be lost', async () => {
    const user = userEvent.setup()
    let deleteCalls = 0
    server.use(
      http.delete(`${API_BASE}/me`, () => {
        deleteCalls += 1
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderAccountSettings()
    await screen.findByTestId('settings-deletion')

    await user.click(screen.getByTestId('delete-account'))

    // The trigger only opens the confirmation — nothing is deleted yet.
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveTextContent(TR('accountSettings.deleteDialogTitle'))
    expect(dialog).toHaveTextContent(TR('accountSettings.deleteDialogDescription'))
    expect(deleteCalls).toBe(0)
  })

  it('does nothing when the confirmation is cancelled', async () => {
    const user = userEvent.setup()
    let deleteCalls = 0
    server.use(
      http.delete(`${API_BASE}/me`, () => {
        deleteCalls += 1
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderAccountSettings()
    await screen.findByTestId('settings-deletion')

    await user.click(screen.getByTestId('delete-account'))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: TR('common.cancel') }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(deleteCalls).toBe(0)
  })
})

describe('Password change — inline validation and session preserved (UXR-ACC-018)', () => {
  function fields() {
    return {
      current: screen.getByLabelText(TR('accountSettings.currentLabel'), {
        exact: false,
      }),
      newPassword: screen.getByLabelText(TR('accountSettings.newPasswordLabel'), {
        exact: false,
      }),
      submit: screen.getByRole('button', {
        name: TR('accountSettings.passwordSubmit'),
      }),
    }
  }

  it('keeps submit disabled while invalid and marks the offending field inline', async () => {
    const user = userEvent.setup()
    renderAccountSettings()
    await screen.findByTestId('settings-password')

    const { current, newPassword, submit } = fields()
    // Nothing entered yet → submit disabled (UXR-G-018).
    expect(submit).toBeDisabled()

    // A short new password fails the policy; the field is marked invalid inline.
    await user.type(current, 'current-password')
    await user.type(newPassword, 'short')
    await user.tab()
    await waitFor(() =>
      expect(newPassword).toHaveAttribute('aria-invalid', 'true'),
    )
    expect(submit).toBeDisabled()

    // A valid pair enables the control.
    await user.clear(newPassword)
    await user.type(newPassword, 'longenough1')
    expect(newPassword).not.toHaveAttribute('aria-invalid', 'true')
    expect(submit).toBeEnabled()
  })

  it('maps a server wrong-current error to the current-password copy and preserves inputs', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/password`, 'VALIDATION_FAILED', {
        method: 'patch',
        params: { fields: [{ field: 'current', code: 'mismatch' }] },
      }),
    )

    renderAccountSettings()
    await screen.findByTestId('settings-password')

    const { current, newPassword, submit } = fields()
    await user.type(current, 'wrong-current')
    await user.type(newPassword, 'longenough1')
    await user.click(submit)

    expect(await screen.findByText(TR('accountSettings.currentMismatch'))).toBeInTheDocument()
    // Inputs are preserved on failure (UXR-G-004).
    expect(current).toHaveValue('wrong-current')
    expect(newPassword).toHaveValue('longenough1')
  })

  it('maps a server weak-new-password error to the minimum-length copy', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/password`, 'VALIDATION_FAILED', {
        method: 'patch',
        params: { fields: [{ field: 'newPassword', code: 'PasswordTooShort' }] },
      }),
    )

    renderAccountSettings()
    await screen.findByTestId('settings-password')

    const { current, newPassword, submit } = fields()
    await user.type(current, 'current-password')
    await user.type(newPassword, 'longenough1')
    await user.click(submit)

    expect(
      await screen.findByText(
        fill(TR('accountSettings.newTooShort'), { min: 10 }),
      ),
    ).toBeInTheDocument()
  })

  it('confirms success, clears the fields, and states the session stays open', async () => {
    const user = userEvent.setup()
    let changeCalls = 0
    server.use(
      http.patch(`${API_BASE}/me/password`, async ({ request }) => {
        changeCalls += 1
        const body = (await request.json()) as {
          current: string
          newPassword: string
        }
        return HttpResponse.json({ email: ME_VERIFIED_FIXTURE.email, ...body })
      }),
    )

    renderAccountSettings()
    await screen.findByTestId('settings-password')

    const { current, newPassword, submit } = fields()
    await user.type(current, 'current-password')
    await user.type(newPassword, 'longenough1')
    await user.click(submit)

    expect(await screen.findByTestId('password-success')).toHaveTextContent(
      TR('accountSettings.passwordSuccess'),
    )
    expect(changeCalls).toBe(1)
    // The fields are cleared after success.
    expect(current).toHaveValue('')
    expect(newPassword).toHaveValue('')
  })
})
