/**
 * Client-side validators for SCR-007 registration (UXR-G-018).
 *
 * These mirror the server contract (`03` §6): a syntactically valid e-mail and
 * the password policy (minimum 10 characters, no composition rules). The server
 * re-validates; these gate the submit control and drive inline copy only.
 */

/** Password policy floor (`03` §6). */
export const PASSWORD_MIN_LENGTH = 10

/**
 * Version of the privacy notice a consent is recorded against (BR-ACC-002).
 *
 * Must be one the API accepts (`Auth` section `NoticeVersions`, `src/Api/Identity/AuthOptions.cs`:
 * server default and `appsettings.json` both serve `"2026-10"`); any other value is
 * rejected 400 `VALIDATION_FAILED` `consent.noticeVersion:"unknown"`.
 */
export const PRIVACY_NOTICE_VERSION = '2026-10'

// Pragmatic e-mail shape check; the server owns the authoritative validation.
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function isValidEmail(value: string): boolean {
  return EMAIL_PATTERN.test(value.trim())
}

export function isValidPassword(value: string): boolean {
  return value.length >= PASSWORD_MIN_LENGTH
}

/**
 * True when `value` is a site-relative path safe to use as an in-app link target
 * (SF3): it must start with a single `/`. `//host` and `/\host` are rejected —
 * the URL parser treats both as the start of a new authority, so following them
 * would navigate cross-origin (`?returnUrl` is attacker-controllable).
 */
export function isSiteRelativePath(value: string): boolean {
  return /^\/(?![/\\])/.test(value)
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
