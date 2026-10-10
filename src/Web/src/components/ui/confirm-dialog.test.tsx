import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { renderWithProviders } from '@/test/render'

function Harness({ onConfirm }: { onConfirm: () => void }) {
  const [open, setOpen] = useState(false)
  return (
    <ConfirmDialog
      open={open}
      onOpenChange={setOpen}
      title="Delete saved screen?"
      description="The screen and its criteria will be permanently removed."
      confirmLabel="Delete"
      cancelLabel="Cancel"
      closeLabel="Close"
      destructive
      onConfirm={onConfirm}
      trigger={<Button>Open</Button>}
    />
  )
}

/**
 * Confirmation dialog (UXR-G-022/026/027). Radix provides the focus trap and
 * focus restoration; these tests assert the declared semantics and the action
 * wiring (the trap itself is verified in the browser).
 */
describe('ConfirmDialog', () => {
  it('opens a labelled modal dialog stating what will be lost', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness onConfirm={() => {}} />)

    await user.click(screen.getByRole('button', { name: 'Open' }))

    const dialog = await screen.findByRole('dialog', {
      name: 'Delete saved screen?',
    })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(
      screen.getByText(
        'The screen and its criteria will be permanently removed.',
      ),
    ).toBeInTheDocument()
  })

  it('invokes onConfirm from the confirm control', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    renderWithProviders(<Harness onConfirm={onConfirm} />)

    await user.click(screen.getByRole('button', { name: 'Open' }))
    await user.click(await screen.findByRole('button', { name: 'Delete' }))

    expect(onConfirm).toHaveBeenCalledTimes(1)
  })

  it('dismisses on cancel and on Escape', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness onConfirm={() => {}} />)

    await user.click(screen.getByRole('button', { name: 'Open' }))
    await user.click(await screen.findByRole('button', { name: 'Cancel' }))
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    )

    await user.click(screen.getByRole('button', { name: 'Open' }))
    await screen.findByRole('dialog')
    await user.keyboard('{Escape}')
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    )
  })
})
