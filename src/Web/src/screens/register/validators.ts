/**
 * Client-side validators for SCR-007 registration (UXR-G-018).
 *
 * These mirror the server contract (`03` §6): a syntactically valid e-mail and
 * the password policy (minimum 10 characters, no composition rules). The server
 * re-validates; these gate the submit control and drive inline copy only.
 */

/** Password policy floor (`03` §6). */
export const PASSWORD_MIN_LENGTH = 10

/** Version of the privacy notice a consent is recorded against (BR-ACC-002). */
export const PRIVACY_NOTICE_VERSION = '2026-10-06'

// Pragmatic e-mail shape check; the server owns the authoritative validation.
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function isValidEmail(value: string): boolean {
  return EMAIL_PATTERN.test(value.trim())
}

export function isValidPassword(value: string): boolean {
  return value.length >= PASSWORD_MIN_LENGTH
}

export interface RegistrationInput {
  email: string
  password: string
  consent: boolean
}

/** Field-level validity of a registration attempt (submit-disabled contract). */
export interface RegistrationValidity {
  email: boolean
  password: boolean
  consent: boolean
  /** True only when every field is valid — the submit control's enabled state. */
  valid: boolean
}

export function validateRegistration(input: RegistrationInput): RegistrationValidity {
  const email = isValidEmail(input.email)
  const password = isValidPassword(input.password)
  const consent = input.consent
  return { email, password, consent, valid: email && password && consent }
}
