import * as React from 'react'

import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

/** Control props `FormField` supplies to the field's control (render-prop). */
export interface FieldControlProps {
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: boolean
  required?: boolean
}

export interface FormFieldProps {
  /** DOM id shared by the label and the control (must be unique per page). */
  id: string
  /** Visible label text (already localized). */
  label: string
  /** Render-prop that returns the control, spreading the supplied props. */
  children: (controlProps: FieldControlProps) => React.ReactNode
  /** Guidance shown before the first invalid submission (UXR-G-018). */
  hint?: string
  /** Inline, plain-language validation message; its presence marks invalid. */
  error?: string
  /** Marks the field as required (visual marker + `required` on the control). */
  required?: boolean
  className?: string
}

/**
 * Form field wrapper (design-system primitive, TKT-foundation-011).
 *
 * Wires the label, an optional hint, and an inline validation message to the
 * control through `id`/`aria-describedby`/`aria-invalid` so validation is
 * programmatically determinable in the active language (UXR-G-018/028). The
 * error message carries `role="alert"` so it is announced when it appears.
 */
export function FormField({
  id,
  label,
  children,
  hint,
  error,
  required,
  className,
}: FormFieldProps) {
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <div data-slot="form-field" className={cn('flex flex-col gap-1.5', className)}>
      <Label htmlFor={id}>
        {label}
        {required ? (
          <span aria-hidden="true" className="text-destructive">
            *
          </span>
        ) : null}
      </Label>
      {children({
        id,
        'aria-describedby': describedBy,
        'aria-invalid': error ? true : undefined,
        required,
      })}
      {hint ? (
        <p id={hintId} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p
          id={errorId}
          role="alert"
          className="text-xs font-medium text-destructive"
        >
          {error}
        </p>
      ) : null}
    </div>
  )
}
