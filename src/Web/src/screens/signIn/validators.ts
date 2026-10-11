/**
 * Client-side validators for SCR-008 sign-in (UXR-G-018).
 *
 * Sign-in only checks that the submission is well-formed: a syntactically valid
 * e-mail and a non-empty password. It deliberately does NOT apply the
 * registration password policy — an existing credential may predate a policy
 * change, and the server is the sole authority on whether the credential is
 * correct (`03` §6). These gate the submit control and drive inline copy only.
 */

// Pragmatic e-mail shape check; the server owns the authoritative validation.
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function isValidEmail(value: string): boolean {
  return EMAIL_PATTERN.test(value.trim())
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

export interface SignInInput {
  email: string
  password: string
}

/** Field-level validity of a sign-in attempt (submit-disabled contract). */
export interface SignInValidity {
  email: boolean
  password: boolean
  /** True only when every field is valid — the submit control's enabled state. */
  valid: boolean
}

export function validateSignIn(input: SignInInput): SignInValidity {
  const email = isValidEmail(input.email)
  const password = input.password.length > 0
  return { email, password, valid: email && password }
}
