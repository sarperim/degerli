import * as React from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'

export interface ConfirmDialogProps {
  /** Controlled open state. */
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Dialog heading (localized). */
  title: string
  /** States plainly what will be lost or changed (UXR-G-022). */
  description: string
  /** Confirm button label (localized). */
  confirmLabel: string
  /** Cancel button label (localized). */
  cancelLabel: string
  /** Localized accessible label for the close control. */
  closeLabel: string
  /** Destructive actions (delete account/screen/scenario) use the red treatment. */
  destructive?: boolean
  /** Shows pending feedback and locks both actions (UXR-G-002/020). */
  pending?: boolean
  onConfirm: () => void
  /** Optional trigger rendered when the dialog owns its own button. */
  trigger?: React.ReactNode
}

/**
 * Confirmation dialog (design-system primitive, TKT-foundation-011).
 *
 * The single reusable confirmation for destructive and consequential actions
 * (UXR-G-022 screen/scenario/account deletion; also used for the admin
 * consequential actions). It states what will be lost, traps focus while open,
 * restores focus to the trigger on close, and locks its actions while pending
 * (UXR-G-002/020/026/027).
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  cancelLabel,
  closeLabel,
  destructive = false,
  pending = false,
  onConfirm,
  trigger,
}: ConfirmDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {trigger ? <DialogTrigger asChild>{trigger}</DialogTrigger> : null}
      <DialogContent closeLabel={closeLabel}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            disabled={pending}
            onClick={() => onOpenChange(false)}
          >
            {cancelLabel}
          </Button>
          <Button
            type="button"
            variant={destructive ? 'destructive' : 'default'}
            aria-busy={pending || undefined}
            disabled={pending}
            onClick={onConfirm}
          >
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
