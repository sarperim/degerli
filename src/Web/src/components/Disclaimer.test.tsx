import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'

import { Disclaimer } from '@/components/Disclaimer'
import { runA11y } from '@/test/a11y'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'

/**
 * Informational-only disclaimer (UXR-G-016). It must be labelled and localized,
 * and must never carry buy/sell framing (UXR-G-017).
 */
describe('Disclaimer', () => {
  it('renders the TR disclaimer text in a labelled region', () => {
    renderWithProviders(<Disclaimer />)
    const region = screen.getByRole('complementary', {
      name: catalogValue('tr', 'disclaimer.label'),
    })
    expect(region).toHaveTextContent(catalogValue('tr', 'disclaimer.text'))
  })

  it('renders the EN disclaimer when English is active', () => {
    renderWithProviders(<Disclaimer />, { language: 'en' })
    expect(
      screen.getByText(catalogValue('en', 'disclaimer.text')),
    ).toBeInTheDocument()
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderWithProviders(<Disclaimer />)
    await expect(runA11y(container)).resolves.toEqual([])
  })
})
