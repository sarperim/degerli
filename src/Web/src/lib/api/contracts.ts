/**
 * Client-side API contracts mirroring `.pipeline/architecture/03-api-design.md`.
 *
 * These types are the web app's view of the server contract: the honest-data
 * envelope (`03` §1.2), the RFC 7807 error-code catalog (`03` §7), and a first
 * slice of public payload shapes (`03` §3). Domain tickets extend this module
 * as they implement their endpoints; the shapes here are what MSW handlers in
 * `src/test/msw` are typed against.
 */

/** Honest-data envelope carried on every data response (`03` §1.2). */
export interface HonestDataEnvelope {
  /** ISO-8601 date the served figures are as of. */
  asOf: string
  /** True when the figure is last-known-good past its freshness window. */
  stale: boolean
}

/** Stable machine error codes from the `03` §7 catalog. */
export const API_ERROR_CODES = [
  'VALIDATION_FAILED',
  'METRIC_NOT_AVAILABLE',
  'UNAUTHENTICATED',
  'INVALID_CREDENTIALS',
  'FORBIDDEN',
  'EMAIL_NOT_VERIFIED',
  'NOT_FOUND',
  'EMAIL_TAKEN',
  'DUPLICATE_NAME',
  'DCF_NOT_COMPUTABLE',
  'TOKEN_EXPIRED',
  'LOCKED_OUT',
  'RATE_LIMITED',
  'INTERNAL',
] as const

export type ApiErrorCode = (typeof API_ERROR_CODES)[number]

/** HTTP status paired with each error code (`03` §7). */
export const API_ERROR_STATUS: Record<ApiErrorCode, number> = {
  VALIDATION_FAILED: 400,
  METRIC_NOT_AVAILABLE: 400,
  UNAUTHENTICATED: 401,
  INVALID_CREDENTIALS: 401,
  FORBIDDEN: 403,
  EMAIL_NOT_VERIFIED: 403,
  NOT_FOUND: 404,
  EMAIL_TAKEN: 409,
  DUPLICATE_NAME: 409,
  DCF_NOT_COMPUTABLE: 422,
  TOKEN_EXPIRED: 410,
  LOCKED_OUT: 429,
  RATE_LIMITED: 429,
  INTERNAL: 500,
}

/** Human-readable problem title per code (`03` §7). Not user-facing prose. */
export const API_ERROR_TITLE: Record<ApiErrorCode, string> = {
  VALIDATION_FAILED: 'Validation failed',
  METRIC_NOT_AVAILABLE: 'Metric not available',
  UNAUTHENTICATED: 'Unauthenticated',
  INVALID_CREDENTIALS: 'Invalid credentials',
  FORBIDDEN: 'Forbidden',
  EMAIL_NOT_VERIFIED: 'E-mail not verified',
  NOT_FOUND: 'Not found',
  EMAIL_TAKEN: 'E-mail taken',
  DUPLICATE_NAME: 'Duplicate name',
  DCF_NOT_COMPUTABLE: 'DCF not computable',
  TOKEN_EXPIRED: 'Token expired',
  LOCKED_OUT: 'Locked out',
  RATE_LIMITED: 'Rate limited',
  INTERNAL: 'Internal error',
}

/** RFC 7807 `application/problem+json` body (`03` §7). */
export interface ProblemDetails {
  type: string
  title: string
  status: number
  code: ApiErrorCode
  params?: Record<string, unknown>
  traceId: string
}

/** Per-series availability for the macro strip (`03` §3). */
export type MacroState = 'available' | 'unavailable'

export interface MacroSeries {
  code: string
  value: number | null
  unit: string
  source: string
  asOf: string
  stale: boolean
  state?: MacroState
}

/** `GET /api/v1/market/macro` (`03` §3). */
export interface MacroResponse extends HonestDataEnvelope {
  series: MacroSeries[]
}

/**
 * A covered-universe row (`GET /api/v1/stocks`, `03` §3; FR-RES-001).
 *
 * `sector` is the sector's stable code (identity, not shape — see the test plan's
 * I-RES-4), or `null` when the instrument is unclassified (UNSEC); the
 * unclassified state is served explicitly and must never be guessed.
 */
export interface StockListItem {
  symbol: string
  name: string
  sector: string | null
  listingDate: string
}

/** `GET /api/v1/stocks?sector=&q=` (`03` §3). */
export interface StockListResponse extends HonestDataEnvelope {
  stocks: StockListItem[]
}

/** A lightweight header-search match (`GET /api/v1/stocks/search`, FR-RES-004). */
export interface StockSearchItem {
  symbol: string
  name: string
}

/** `GET /api/v1/stocks/search?q=` (`03` §3). */
export interface StockSearchResponse extends HonestDataEnvelope {
  stocks: StockSearchItem[]
}

/* ------------------------------------------------------------------ *
 * Identity (`03` §6) — TKT-acc-007 (SCR-007 register + verification).
 * ------------------------------------------------------------------ */

/** Language preference values the API accepts (`03` §6, `01` §10.6). */
export type LanguagePref = 'tr' | 'en'

/** `GET /api/v1/auth/session` — authenticated branch (`03` §6). */
export interface AuthenticatedSession {
  authenticated: true
  email: string
  /** E-mail verification gates persistence (UXR-G-030 / UXR-ACC-007). */
  verified: boolean
  languagePref: LanguagePref
  role: string
}

/** `GET /api/v1/auth/session` — anonymous branch (`03` §6). */
export interface AnonymousSession {
  authenticated: false
}

export type SessionResponse = AuthenticatedSession | AnonymousSession

/** `POST /api/v1/auth/register` request body (`03` §6). */
export interface RegisterRequest {
  email: string
  password: string
  consent: { noticeVersion: string }
}

/**
 * `POST /api/v1/auth/register` response (`03` §6) — 201 with the created account
 * e-mail. Registration signs the caller in, so the session is refreshed via
 * `GET /auth/session` rather than returned in this body.
 */
export interface RegisterResponse {
  email: string
}

/** `POST /api/v1/auth/verify-email` request body (`03` §6). */
export interface VerifyEmailRequest {
  token: string
}

/** `POST /api/v1/auth/resend-verification` response (`03` §6) — 200 `{verified}`. */
export interface ResendVerificationResponse {
  verified: boolean
}

/** `GET /api/v1/auth/csrf-token` response (`03` §2) — antiforgery token + header. */
export interface CsrfTokenResponse {
  token: string
  headerName: string
}
