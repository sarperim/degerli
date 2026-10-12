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

/** `POST /api/v1/auth/login` request body (`03` §6). */
export interface LoginRequest {
  email: string
  password: string
}

/**
 * `POST /api/v1/auth/login` response (`03` §6) — 200 with the session cookie and
 * the account's stored language preference, applied client-side (UXR-ACC-009).
 */
export interface LoginResponse {
  languagePref: LanguagePref
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

/* ------------------------------------------------------------------ *
 * Screener (`03` §4) — TKT-scr-006 (SCR-003 criteria builder + run + save).
 * ------------------------------------------------------------------ */

/** The five metric families the criteria builder groups by (UXR-SCR-001). */
export type MetricFamily =
  | 'valuation'
  | 'quality'
  | 'growth'
  | 'financial_health'
  | 'dividends'

/** One screenable metric in the builder catalog (`GET /screener/metrics`). */
export interface CatalogMetric {
  metricCode: string
  /** Display unit: `%` metrics carry values as fractions (0.15 = 15%). */
  unit: string
  labelTr: string
  labelEn: string
  /** Growth metrics require a CAGR window (FR-SCR-015). */
  isCagr: boolean
  sortOrder: number
}

export interface MetricFamilyGroup {
  family: MetricFamily
  metrics: CatalogMetric[]
}

/** `GET /api/v1/screener/metrics` — visible catalog only (UXR-SCR-011). */
export interface MetricCatalogResponse {
  families: MetricFamilyGroup[]
}

/** Bound type for one criterion (FR-SCR-003). */
export type CriterionBound = 'min' | 'max' | 'range'

/** The selectable CAGR windows (BR-SCR-005). */
export type CagrWindow = 3 | 5 | 10

/**
 * One criterion on the wire (`03` §4). Values are canonical: `%`-unit metrics are
 * fractions (0.15), ratio metrics are raw (15). The UI converts percentage-point
 * input to fractions at this boundary.
 */
export interface ScreenerCriterion {
  metricCode: string
  bound: CriterionBound
  minValue?: number
  maxValue?: number
  window?: CagrWindow
}

/** `POST /api/v1/screener/run` request body (`03` §4). */
export interface ScreenerRunRequest {
  criteria: ScreenerCriterion[]
}

/** One matching stock row; `values` is keyed by the concept metric code. */
export interface ScreenerRunRow {
  symbol: string
  name: string
  sector: string | null
  values: Record<string, number | null>
}

/** `POST /api/v1/screener/run` response (`03` §4). Emptiness is data, not an error. */
export interface ScreenerRunResponse extends HonestDataEnvelope {
  matchCount: number
  excludedCount: number
  /** Metrics dropped from a re-run because they are no longer offered. */
  droppedCriteria: string[]
  rows: ScreenerRunRow[]
}

/** A saved screen as served by the `/me/screens` endpoints (`03` §4). */
export interface SavedScreen {
  id: number
  name: string
  criteria: ScreenerCriterion[]
  createdAt: string
  updatedAt: string
}

/** `GET /api/v1/me/screens` response (`03` §4). */
export interface SavedScreenListResponse {
  screens: SavedScreen[]
}

/** `POST /api/v1/me/screens` request body (`03` §4). */
export interface SaveScreenRequest {
  name: string
  criteria: ScreenerCriterion[]
}

/* ------------------------------------------------------------------ *
 * Valuation & DCF (`03` §5) — TKT-val-006 (SCR-006 calculator UI).
 * ------------------------------------------------------------------ */

/** The eight confirmed, fully user-editable DCF parameters (OQ-UX-001, UX §4.1). */
export type DcfParamKey =
  | 'base_fcf'
  | 'growth_rate'
  | 'horizon_years'
  | 'terminal_growth'
  | 'discount_rate'
  | 'debt'
  | 'cash'
  | 'share_count'

/** The parameter set on the wire: canonical values (rates as fractions, 0.12). */
export type DcfParams = Record<DcfParamKey, number>

/** Per canonical-fact provenance — as-of + restatement flag (UXR-VAL-010). */
export interface CanonicalFactRef {
  asOf: string
  restated: boolean
}

/** The active per-stock baseline carried by `GET /stocks/{symbol}/dcf`. */
export interface DcfBaseline {
  version: number
  buildDate: string
  params: DcfParams
  /** Provenance for the statement-derived defaults (canonical facts only). */
  canonicalFactRefs: Partial<Record<DcfParamKey, CanonicalFactRef>>
}

/** Canonical EOD price carried by both DCF payloads (NFR-VAL-003). */
export interface DcfPrice {
  value: number
  asOf: string
  stale: boolean
}

/**
 * `GET /api/v1/stocks/{symbol}/dcf` (`03` §5; UC-VAL-001 steps 1–2).
 *
 * When a statement-derived input is missing the payload is `200` with
 * `state: "missing_inputs"` naming what is missing instead of computing on
 * partial data (UXR-VAL-009); the missing-inputs UI is owned by TKT-val-007.
 */
export interface DcfBaselineResponse {
  symbol: string
  baseline?: DcfBaseline
  price?: DcfPrice
  state?: 'missing_inputs'
  missingInputs?: DcfParamKey[]
}

/** `POST /api/v1/stocks/{symbol}/dcf/compute` request — the eight parameters. */
export type DcfComputeRequest = DcfParams

/**
 * The 5×5 sensitivity grid (FR-VAL-007, SD-001): fair value across
 * discount-rate (rows) × terminal-growth (columns); a `null` cell is honestly
 * not computable (r ≤ g_t, I-VAL-1).
 */
export interface DcfSensitivity {
  discountRates: number[]
  terminalGrowths: number[]
  fairValues: (number | null)[][]
}

/** `POST /api/v1/stocks/{symbol}/dcf/compute` response (`03` §5). */
export interface DcfComputeResponse {
  fairValuePerShare: number
  marginOfSafety: number
  price: DcfPrice
  sensitivity: DcfSensitivity
}

/** A saved DCF scenario as served by the `/me/scenarios` endpoints (`03` §5). */
export interface SavedScenario {
  id: number
  symbol: string
  name: string
  /** The eight parameters, stored verbatim so a load restores them exactly. */
  params: DcfParams
  createdAt: string
  updatedAt: string
}

/** `GET /api/v1/me/scenarios?symbol=` response (`03` §5). */
export interface SavedScenarioListResponse {
  scenarios: SavedScenario[]
}

/** `POST /api/v1/me/scenarios` request body (`03` §5) — save from SCR-006. */
export interface SaveScenarioRequest {
  symbol: string
  name: string
  params: DcfParams
}
