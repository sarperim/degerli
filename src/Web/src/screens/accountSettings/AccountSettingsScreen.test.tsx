import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router'

import { SignInScreen } from '@/screens/signIn/SignInScreen'
import { catalogValue } from '@/test/i18n'
import {
  ANONYMOUS_SESSION_FIXTURE,
  API_BASE,
  UNVERIFIED_SESSION_FIXTURE,
  VERIFIED_SESSION_FIXTURE,
  loginHandler,
  meHandler,
  screenerMetricsHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'
import {
  ME_UNVERIFIED_FIXTURE,
  ME_VERIFIED_FIXTURE,
  renderAccountSettings,
  resetAccountSettingsTest,
} from '@/screens/accountSettings/testSupport'

/**
 * SCR-010 — Account Settings (L3/L4, test strategy §5.3).
 *
 * TC-ACC-025 — the screen contains exactly the required sections (language
 * preference, password change, entry points, deletion, verification status +
 * resend while unverified) and nothing else; anonymous direct access presents
 * the sign-in path (UXR-ACC-016, UXR-ACC-020).
 * TC-ACC-026 — account deletion sits behind a focus-trapping confirmation that
 * states saved screens and DCF scenarios will be lost; cancel does nothing
 * (UXR-ACC-019, UXR-G-022).
 * TC-ACC-029 — setting English applies immediately and persists to the account;
 * an independent context applies the stored preference at sign-in
 * (BR-ACC-004, UXR-ACC-017).
 * TC-ACC-032 — deleting the account removes it, signs the user out, and leaves
 * previously saved screens inaccessible (UC-ACC-003 alternate a).
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

describe('TC-ACC-029 — language preference across devices', () => {
  it('applies English immediately in settings and persists it to the account', async () => {
    const user = userEvent.setup()
    let captured: unknown = null
    server.use(
      http.patch(`${API_BASE}/me`, async ({ request }) => {
        captured = await request.json()
        return HttpResponse.json({ ...ME_VERIFIED_FIXTURE, languagePref: 'en' })
      }),
    )

    const { i18n } = renderAccountSettings()
    await screen.findByTestId('settings-language')

    await user.click(screen.getByRole('button', { name: TR('header.english') }))

    // Immediate UI switch (no reload) …
    expect(i18n.resolvedLanguage).toBe('en')
    // … and persisted to the account.
    await waitFor(() => expect(captured).toEqual({ languagePref: 'en' }))
  })

  it('applies the stored account preference on sign-in from an independent context', async () => {
    const user = userEvent.setup()
    server.use(loginHandler({ languagePref: 'en' }))

    // An independent context: a fresh render with its own i18n instance.
    const { i18n } = renderWithProviders(
      <MemoryRouter initialEntries={['/sign-in']}>
        <Routes>
          <Route path="/sign-in" element={<SignInScreen />} />
          <Route path="/" element={<div data-testid="home" />} />
        </Routes>
      </MemoryRouter>,
    )

    await user.type(
      screen.getByLabelText(TR('signIn.emailLabel'), { exact: false }),
      'ayse@example.test',
    )
    await user.type(
      screen.getByLabelText(TR('signIn.passwordLabel'), { exact: false }),
      'correct-horse-battery',
    )
    await user.click(screen.getByRole('button', { name: TR('signIn.submit') }))

    await waitFor(() => expect(i18n.resolvedLanguage).toBe('en'))
  })
})

describe('TC-ACC-032 — delete account end-to-end', () => {
  it('removes the account, signs the user out, and leaves saved screens inaccessible', async () => {
    const user = userEvent.setup()
    let deleted = false
    server.use(
      http.get(`${API_BASE}/auth/session`, () =>
        HttpResponse.json(
          deleted ? ANONYMOUS_SESSION_FIXTURE : VERIFIED_SESSION_FIXTURE,
        ),
      ),
      http.delete(`${API_BASE}/me`, () => {
        deleted = true
        return new HttpResponse(null, { status: 204 })
      }),
      meHandler(ME_VERIFIED_FIXTURE),
      screenerMetricsHandler(),
    )

    renderAccountSettings()
    await screen.findByTestId('settings-deletion')

    await user.click(screen.getByTestId('delete-account'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent(TR('accountSettings.deleteDialogDescription'))
    await user.click(
      within(dialog).getByRole('button', { name: TR('accountSettings.deleteConfirm') }),
    )

    // Account removed; the user is signed out and returned to an anonymous home.
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/$/))
    expect(deleted).toBe(true)

    // Previously saved screens are no longer accessible — the list presents the
    // sign-in path for the now-anonymous session.
    await user.click(screen.getByRole('link', { name: 'go-screens' }))
    expect(await screen.findByTestId('saved-screens-anonymous')).toBeInTheDocument()
  })
})
