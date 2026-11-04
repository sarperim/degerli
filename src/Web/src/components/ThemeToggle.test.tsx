import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { ThemeToggle } from '@/components/ThemeToggle'
import { setTheme, THEME_STORAGE_KEY } from '@/lib/theme'
import { runA11y } from '@/test/a11y'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'

describe('ThemeToggle', () => {
  beforeEach(() => {
    setTheme('light')
    window.localStorage.removeItem(THEME_STORAGE_KEY)
  })

  afterEach(() => {
    setTheme('light')
    window.localStorage.removeItem(THEME_STORAGE_KEY)
  })

  it('renders in light mode by default and toggles to dark and back', async () => {
    const user = userEvent.setup()
    const { container } = renderWithProviders(<ThemeToggle />)

    const button = screen.getByRole('button', {
      name: new RegExp(catalogValue('tr', 'header.themeToggle')),
    })

    expect(button).toHaveAttribute('aria-pressed', 'false')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
    expect(button).toHaveTextContent(catalogValue('tr', 'header.lightMode'))
    expect(await runA11y(container)).toEqual([])

    await user.click(button)

    expect(button).toHaveAttribute('aria-pressed', 'true')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
    expect(button).toHaveTextContent(catalogValue('tr', 'header.darkMode'))
    expect(await runA11y(container)).toEqual([])

    await user.click(button)

    expect(button).toHaveAttribute('aria-pressed', 'false')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
    expect(button).toHaveTextContent(catalogValue('tr', 'header.lightMode'))
  })

  it('renders localized copy in English catalog', () => {
    renderWithProviders(<ThemeToggle />, { language: 'en' })

    const button = screen.getByRole('button', {
      name: new RegExp(catalogValue('en', 'header.themeToggle')),
    })

    expect(button).toHaveAttribute('aria-pressed', 'false')
    expect(button).toHaveTextContent(catalogValue('en', 'header.lightMode'))
  })
})
