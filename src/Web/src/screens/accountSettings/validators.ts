import { PASSWORD_MIN_LENGTH } from '@/screens/register/validators'

/**
 * Client-side validators for SCR-010 password change (UXR-ACC-018, UXR-G-018).
 *
 * These mirror the server contract (`03` §6): the current password must be
 * present and the new password must satisfy the account password policy (minimum
 * 10 characters, no composition rules). The server re-validates; these gate the
 * submit control and drive inline copy only. The current password's *correctness*
 * is never checked client-side — it is the server's authority (I-ACC-1).
 */

export { PASSWORD_MIN_LENGTH }

export interface PasswordChangeInput {
  current: string
  newPassword: string
}

/** Field-level validity of a password change (submit-disabled contract). */
export interface PasswordChangeValidity {
  current: boolean
  newPassword: boolean
  /** True only when every field is valid — the submit control's enabled state. */
  valid: boolean
}

export function validatePasswordChange(
  input: PasswordChangeInput,
): PasswordChangeValidity {
  const current = input.current.length > 0
  const newPassword = input.newPassword.length >= PASSWORD_MIN_LENGTH
  return { current, newPassword, valid: current && newPassword }
}

/** One field error from the RFC 7807 `VALIDATION_FAILED` payload (`03` §7). */
export interface ValidationField {
  field: string
  code: string
}
