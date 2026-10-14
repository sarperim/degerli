import { describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http } from 'msw'

import { catalogValue } from '@/test/i18n'
import { API_BASE, problemResponse } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import {
  renderAccountSettings,
  resetAccountSettingsTest,
} from '@/screens/accountSettings/testSupport'

/**
 * TC-XC-005 (L3, MSW-driven failure variant) — language persistence: the
 * signed-in sync leg (`.pipeline/testing/cross-cutting.md` §2; UXR-G-014,
 * BR-ACC-004).
 *
 * A signed-in language change applies optimistically and issues `PATCH /me`; if
 * the save fails the UI reverts to the previous language and surfaces a notice —
 * the account preference never silently diverges from the shown language. The
 * device-persistence legs (anonymous choice surviving reload and a new visit) and
 * the cross-device account-preference leg are L4 and live in
 * `tests/e2e/src/global-ux.spec.ts`.
 */

const TR = (key: string) => catalogValue('tr', key)

describe('TC-XC-005 — language persistence (signed-in sync failure)', () => {
  it('applies optimistically, then reverts with a notice when PATCH /me fails', async () => {
    resetAccountSettingsTest()
    const user = userEvent.setup()

    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    let patchCalls = 0
    server.use(
      http.patch(`${API_BASE}/me`, async () => {
        patchCalls += 1
        await gate
        return problemResponse('INTERNAL')
      }),
    )

    const { i18n } = renderAccountSettings()
    await screen.findByTestId('settings-language')

    const group = screen.getByRole('group', {
      name: TR('accountSettings.languageLabel'),
    })
    await user.click(
      within(group).getByRole('button', { name: TR('header.english') }),
    )

    // Optimistic: the UI switches immediately and the PATCH is in flight.
    await waitFor(() => expect(i18n.resolvedLanguage).toBe('en'))
    expect(patchCalls).toBe(1)

    // The save fails → revert to Turkish and surface the notice.
    release()

    expect(await screen.findByTestId('language-error')).toHaveTextContent(
      TR('accountSettings.languageSaveFailed'),
    )
    await waitFor(() => expect(i18n.resolvedLanguage).toBe('tr'))

    // The toggle reflects the reverted state: Turkish is active again.
    const reverted = screen.getByRole('group', {
      name: TR('accountSettings.languageLabel'),
    })
    expect(
      within(reverted).getByRole('button', { name: TR('header.turkish') }),
    ).toHaveAttribute('aria-pressed', 'true')
    expect(
      within(reverted).getByRole('button', { name: TR('header.english') }),
    ).toHaveAttribute('aria-pressed', 'false')
  })
})
