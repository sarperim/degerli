import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'

import { FormField } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { runA11y } from '@/test/a11y'
import { renderWithProviders } from '@/test/render'

/**
 * Inputs with validation states (UXR-G-018). The inline error must be
 * programmatically associated with the control and mark it invalid.
 */
describe('FormField + Input validation states', () => {
  it('associates the label, hint and control by id', () => {
    renderWithProviders(
      <FormField id="email" label="E-mail" hint="We never share it.">
        {(control) => <Input {...control} />}
      </FormField>,
    )

    const input = screen.getByLabelText('E-mail')
    expect(input).toHaveAttribute('id', 'email')
    expect(input).toHaveAccessibleDescription('We never share it.')
    expect(input).not.toHaveAttribute('aria-invalid')
  })

  it('marks the control invalid and announces the error inline', () => {
    renderWithProviders(
      <FormField id="email" label="E-mail" error="Enter a valid e-mail address.">
        {(control) => <Input {...control} />}
      </FormField>,
    )

    const input = screen.getByLabelText('E-mail')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAccessibleDescription('Enter a valid e-mail address.')
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Enter a valid e-mail address.',
    )
  })

  it('exposes the required state on the control', () => {
    renderWithProviders(
      <FormField id="email" label="E-mail" required>
        {(control) => <Input {...control} />}
      </FormField>,
    )
    expect(screen.getByLabelText(/E-mail/)).toBeRequired()
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderWithProviders(
      <FormField id="email" label="E-mail" error="Required." required>
        {(control) => <Input {...control} />}
      </FormField>,
    )
    await expect(runA11y(container)).resolves.toEqual([])
  })
})
