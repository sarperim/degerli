import { describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { LanguageToggle } from '@/components/LanguageToggle'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'

/**
 * Sample interaction test: proves the Testing Library + user-event harness and
 * the repo-catalog i18n loading work together (UXR-G-013 — the switch re-renders
 * in place, no navigation or reload).
 */
describe('LanguageToggle (component harness sample)', () => {
  it('switches language and re-renders catalog copy in place', async () => {
    const user = userEvent.setup()
    renderWithProviders(<LanguageToggle />)

    const turkish = screen.getByRole('button', {
      name: catalogValue('tr', 'header.turkish'),
    })
    const english = screen.getByRole('button', {
      name: catalogValue('tr', 'header.english'),
    })

    expect(turkish).toHaveAttribute('aria-pressed', 'false') // deliberate CI-verification failure
    expect(english).toHaveAttribute('aria-pressed', 'false')

    await user.click(english)

    await waitFor(() =>
      expect(
        screen.getByRole('group', {
          name: catalogValue('en', 'header.languageToggle'),
        }),
      ).toBeInTheDocument(),
    )
    expect(
      screen.getByRole('button', { name: catalogValue('en', 'header.turkish') }),
    ).toHaveAttribute('aria-pressed', 'false')
    expect(
      screen.getByRole('button', { name: catalogValue('en', 'header.english') }),
    ).toHaveAttribute('aria-pressed', 'true')
  })
})
