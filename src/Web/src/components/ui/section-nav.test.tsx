import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { SectionNav } from '@/components/ui/section-nav'
import { runA11y } from '@/test/a11y'
import { renderWithProviders } from '@/test/render'

const ITEMS = [
  { id: 'overview', label: 'Overview' },
  { id: 'valuation', label: 'Valuation' },
  { id: 'financials', label: 'Financials' },
]

/**
 * Section navigation (UXR-G-021/025/026/028): the active section is exposed via
 * `aria-current`, every section is reachable and operable by keyboard, and the
 * landmark is labelled.
 */
describe('SectionNav', () => {
  it('exposes the active section with aria-current and a labelled landmark', () => {
    renderWithProviders(
      <SectionNav
        items={ITEMS}
        activeId="valuation"
        onSelect={() => {}}
        ariaLabel="Stock page sections"
      />,
    )

    expect(
      screen.getByRole('navigation', { name: 'Stock page sections' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Valuation' })).toHaveAttribute(
      'aria-current',
      'true',
    )
    expect(screen.getByRole('button', { name: 'Overview' })).not.toHaveAttribute(
      'aria-current',
    )
  })

  it('selects a section on click and by keyboard', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    renderWithProviders(
      <SectionNav
        items={ITEMS}
        activeId="overview"
        onSelect={onSelect}
        ariaLabel="Stock page sections"
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Financials' }))
    expect(onSelect).toHaveBeenLastCalledWith('financials')

    screen.getByRole('button', { name: 'Valuation' }).focus()
    await user.keyboard('{Enter}')
    expect(onSelect).toHaveBeenLastCalledWith('valuation')
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderWithProviders(
      <SectionNav
        items={ITEMS}
        activeId="overview"
        onSelect={() => {}}
        ariaLabel="Stock page sections"
      />,
    )
    await expect(runA11y(container)).resolves.toEqual([])
  })
})
