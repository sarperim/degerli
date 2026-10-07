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
