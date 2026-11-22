import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'

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
      screen.getByRole('button', { name: catalogValue('tr', 'header.signIn') }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: catalogValue('tr', 'header.register') }),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: catalogValue('tr', 'header.signOut') }),
    ).not.toBeInTheDocument()
    expect(screen.queryByTestId('auth-email')).not.toBeInTheDocument()
  })

  it('renders EN catalog copy when English is active', () => {
    renderWithProviders(<AuthState />, { language: 'en' })

    expect(
      screen.getByRole('button', { name: catalogValue('en', 'header.signIn') }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: catalogValue('en', 'header.register') }),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: catalogValue('en', 'header.signOut') }),
    ).not.toBeInTheDocument()
    expect(screen.queryByTestId('auth-email')).not.toBeInTheDocument()
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderWithProviders(<AuthState />)

    await expect(runA11y(container)).resolves.toEqual([])
  })

  it('navigates in-app to /sign-in and /register without window.location.assign when inside a router', async () => {
    const user = userEvent.setup()
    const assignMock = vi.fn()
    vi.stubGlobal('location', { ...window.location, assign: assignMock })

    try {
      renderWithProviders(
        <MemoryRouter initialEntries={['/']}>
          <AuthState />
          <Routes>
            <Route path="/" element={<div data-testid="route-home" />} />
            <Route path="/sign-in" element={<div data-testid="route-sign-in" />} />
            <Route path="/register" element={<div data-testid="route-register" />} />
          </Routes>
        </MemoryRouter>,
      )

      await user.click(
        screen.getByRole('button', { name: catalogValue('tr', 'header.signIn') }),
      )
      expect(await screen.findByTestId('route-sign-in')).toBeInTheDocument()

      await user.click(
        screen.getByRole('button', { name: catalogValue('tr', 'header.register') }),
      )
      expect(await screen.findByTestId('route-register')).toBeInTheDocument()
      expect(assignMock).not.toHaveBeenCalled()
    } finally {
      vi.unstubAllGlobals()
    }
  })
})
