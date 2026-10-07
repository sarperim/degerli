import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'

import { AuthState } from '@/components/AuthState'
import { runA11y } from '@/test/a11y'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'

/**
 * Sample L3 component test (test strategy §5.3).
 *
 * Copy is asserted from the checked-in `tr.json`/`en.json` catalogs via
 * `catalogValue` — never hardcoded prose (§8.3).
 */
describe('AuthState (component harness sample)', () => {
  it('renders the anonymous-state copy from the TR catalog', () => {
    renderWithProviders(<AuthState />)

    expect(
      screen.getByText(catalogValue('tr', 'header.anonymous')),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: catalogValue('tr', 'header.signIn') }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: catalogValue('tr', 'header.register') }),
    ).toBeInTheDocument()
  })

  it('renders EN catalog copy when English is active', () => {
    renderWithProviders(<AuthState />, { language: 'en' })

    expect(
      screen.getByText(catalogValue('en', 'header.anonymous')),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: catalogValue('en', 'header.signIn') }),
    ).toBeInTheDocument()
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderWithProviders(<AuthState />)

    await expect(runA11y(container)).resolves.toEqual([])
  })
})
