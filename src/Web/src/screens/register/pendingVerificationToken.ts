/**
 * Module-scoped in-memory holder for an e-mail verification token while the user
 * completes an unauthenticated sign-in redirect or retries after rate-limiting.
 *
 * Stored ONLY in module memory — never `localStorage`, `sessionStorage`, or URL
 * parameters — so the raw token is never persisted or leaked across tabs/history (S3).
 */
let pendingToken: string | null = null

export function setPendingVerificationToken(token: string): void {
  pendingToken = token
}

export function peekPendingVerificationToken(): string | null {
  return pendingToken
}

export function clearPendingVerificationToken(): void {
  pendingToken = null
}
