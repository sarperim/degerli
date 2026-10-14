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
 * Identity — password reset (`03` §6) — TKT-acc-009 (SCR-009).
 * ------------------------------------------------------------------ */

/** `POST /api/v1/auth/forgot-password` request body (`03` §6). */
export interface ForgotPasswordRequest {
  email: string
}

/**
 * `POST /api/v1/auth/forgot-password` response (`03` §6) — always a byte-identical
 * 200 neutral confirmation; the client renders fixed copy and never branches on it
 * (UXR-ACC-012, enumeration-neutral).
 */
export interface ForgotPasswordResponse {
  status: string
}

/** `POST /api/v1/auth/reset-password` request body (`03` §6). */
export interface ResetPasswordRequest {
  token: string
  newPassword: string
}

/**
 * `POST /api/v1/auth/reset-password` response (`03` §6) — 200 on success; the
 * endpoint may establish a session, so `['session']` is invalidated (UXR-ACC-015).
 */
export interface ResetPasswordResponse {
  status: string
}

/* ------------------------------------------------------------------ *
 * Account settings (`03` §6) — TKT-acc-010 (SCR-010).
 * ------------------------------------------------------------------ */

/**
 * `GET /api/v1/me` and the response of the account mutations (`03` §6) — the
 * minimal account read (BR-ACC-001/008, NFR-ACC-001): e-mail, stored language
 * preference and e-mail verification status only.
 */
export interface MeResponse {
  email: string
  languagePref: LanguagePref
  verified: boolean
}

/** `PATCH /api/v1/me` request body (`03` §6) — the language preference. */
export interface UpdateLanguagePreferenceRequest {
  languagePref: LanguagePref
}

/** `PATCH /api/v1/me/password` request body (`03` §6). */
export interface PasswordChangeRequest {
  current: string
  newPassword: string
}

/** `PATCH /api/v1/me/password` response (`03` §6) — the account e-mail. */
export interface PasswordChangeResponse {
  email: string
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

/**
 * `POST /api/v1/screener/run` request body (`03` §4).
 *
 * An ad-hoc run ships `criteria` directly. A saved-screen re-run also names the
 * screen with `savedScreenId` (which the server checks the caller owns): this marks
 * the run as a re-run, so the server drops any criterion whose metric has since been
 * hidden and reports it in `droppedCriteria` (UC-SCR-003a, UXR-SCR-015) instead of the
 * ad-hoc `METRIC_NOT_AVAILABLE` rejection. The re-run still ships the stored criteria.
 */
export interface ScreenerRunRequest {
  criteria?: ScreenerCriterion[]
  savedScreenId?: number
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
 * Stock page sections (`03-api-design.md` §3) — TKT-res-008 (SCR-005).
 *
 * Each of SCR-005's sections is its own endpoint with its own honest-data
 * envelope (M-1/M-5): a data payload or an explicit missing-data payload
 * (`state` + optional `coverage`) — never a blank treated as zero
 * (BR-RES-008, UXR-G-006). The sections load, fail and retry independently.
 * ------------------------------------------------------------------ */

/** The three explicit missing-data states (architecture §10.7). */
export type DataState = 'no-data' | 'preparing' | 'unavailable'

/** Coverage boundary accompanying a missing-data payload (UXR-RES-022). */
export interface DataCoverage {
  availableFrom: string | null
  boundaryNote: string | null
}

/** A missing-data response: `state` (+ `coverage`) instead of the data shape. */
export interface MissingDataResponse extends HonestDataEnvelope {
  state: DataState
  coverage: DataCoverage | null
}

/** `GET /api/v1/stocks/{symbol}/overview` (`03` §3; FR-RES-005/022/026). */
export interface StockOverviewResponse extends HonestDataEnvelope {
  status: 'published' | 'preparing'
  textTr?: string | null
  textEn?: string | null
  lastReviewedAt?: string | null
}

/** A figure with its per-figure restatement marker (FR-RES-008/014). */
export interface FinancialFigure {
  value: number | null
  restated: boolean
  /** Set to `no-data` when the figure is genuinely absent (never blank-as-zero). */
  state?: DataState
}

/** One period of the Financials table (revenue → EBITDA → EBIT → NI → FCF). */
export interface FinancialPeriod {
  periodType: string
  fiscalYear: number
  periodEndDate: string
  revenue: FinancialFigure
  ebitda: FinancialFigure
  ebit: FinancialFigure
  netIncome: FinancialFigure
  fcf: FinancialFigure
}

/** `GET /api/v1/stocks/{symbol}/financials` (`03` §3; FR-RES-008). */
export interface StockFinancialsResponse extends HonestDataEnvelope {
  periods: FinancialPeriod[]
}

/** A canonical metric value; `null` is explicitly not-meaningful (BR-RES-008). */
export interface MetricValue {
  value: number | null
  notMeaningful: boolean
}

/** `GET /api/v1/stocks/{symbol}/profitability` (`03` §3; FR-RES-009). */
export interface StockProfitabilityResponse extends HonestDataEnvelope {
  /** Keyed by concept code: `roic`, `roe`, `gross_margin`, `operating_margin`. */
  metrics: Record<string, MetricValue>
}

/** One CAGR entry: the requested `window` and the `windowYears` actually used. */
export interface GrowthWindow {
  window: number
  windowYears: number | null
  value: number | null
}

/** `GET /api/v1/stocks/{symbol}/growth` (`03` §3; FR-RES-010/016). */
export interface StockGrowthResponse extends HonestDataEnvelope {
  /** Keyed by growth family: `rev`, `eps`, `fcf`, `div`. */
  metrics: Record<string, GrowthWindow[]>
}

/** A balance-sheet line / aggregate figure with its restatement marker. */
export type BalanceSheetFigure = FinancialFigure

/** `GET /api/v1/stocks/{symbol}/balance-sheet` (`03` §3; FR-RES-011/027). */
export interface StockBalanceSheetResponse extends HonestDataEnvelope {
  periodType: string
  fiscalYear: number
  periodEndDate: string
  restated: boolean
  /** Keyed by line code: `cash`, `shortTermDebt`, `longTermDebt`, … */
  lines: Record<string, BalanceSheetFigure>
  bookValue: BalanceSheetFigure
  bookValuePerShare: BalanceSheetFigure
}

/** One dated dividend payment (`03` §3; FR-RES-012). */
export interface DividendRow {
  exDate: string
  payDate: string
  amountPerShare: number
  currency: string
}

/** `GET /api/v1/stocks/{symbol}/dividends` (`03` §3; FR-RES-012). */
export interface StockDividendsResponse extends HonestDataEnvelope {
  dividends: DividendRow[]
  ttmPerShare?: number
}

/* ------------------------------------------------------------------ *
 * Valuation section (`03` §3) — TKT-res-009 (SCR-005 §2).
 *
 * The server (`StockOverviewAndValuationEndpoints`) serves the current
 * canonical multiples, the historical price series with its honest
 * corporate-action-adjusted basis flag, and the vs.-sector strip. Raw and
 * adjusted closes coexist so a later split never fabricates a cliff
 * (BR-MDF-010); the strip carries the peer/exclusion counts that make the
 * sector median honest (FR-RES-007, UXR-RES-024/025).
 * ------------------------------------------------------------------ */

/** One dated close in the valuation history; raw and adjusted coexist. */
export interface ValuationPricePoint {
  date: string
  closeRaw: number
  /** Null when no adjusted close was ingested — never a raw value presented as adjusted. */
  closeAdjusted: number | null
}

/** The historical price series and its honest basis flag (FR-RES-006/013). */
export interface ValuationHistoricalSeries {
  /** True only when every served row carries an adjusted close (BR-MDF-010). */
  adjusted: boolean
  series: ValuationPricePoint[]
}

/**
 * One vs.-sector strip entry (`03` §3; UXR-RES-013/024/025).
 *
 * `value` null is explicitly not-meaningful, never 0 (BR-RES-008); a null
 * `sectorMedian` degrades that metric to an honest no-data state rather than a
 * fabricated comparison.
 */
export interface SectorStripEntry {
  value: number | null
  notMeaningful: boolean
  sectorMedian: number | null
  peerCount: number | null
  excludedCount: number | null
}

/** `GET /api/v1/stocks/{symbol}/valuation` (`03` §3; FR-RES-006/007). */
export interface StockValuationResponse extends HonestDataEnvelope {
  /** Current canonical multiples keyed by concept code (`pe`, `pb`, …); null = not meaningful. */
  current: Record<string, number | null>
  historical: ValuationHistoricalSeries
  /** The five valuation-family strip entries, keyed by concept code. */
  strip: Record<string, SectorStripEntry>
}

/* ------------------------------------------------------------------ *
 * Admin / SCR-012 (`03` §8, AD-13) — TKT-mdf-012 (MDF sections of the
 * admin shell). Descriptions / metric-visibility / baselines sections are
 * owned by TKT-res-010, TKT-scr-005, TKT-val-009 and extend these types
 * in their own modules.
 * ------------------------------------------------------------------ */

/** Terminal vs non-terminal ingest-run statuses (`03` §8; IngestResult). */
export const INGEST_TERMINAL_STATUSES = ['succeeded', 'failed', 'partial'] as const

/** One job's most recent run in the ops summary (UXR-MDF-003). */
export interface AdminJobSummary {
  job: string
  lastRun: { status: string; finishedAt: string | null } | null
}

/** One data type's freshness/staleness in the ops summary (UXR-MDF-004). */
export interface AdminFreshness {
  dataType: string
  lastSuccess: string | null
  stale: boolean
}

/** `GET /api/v1/admin/summary` (`03` §8). */
export interface AdminSummaryResponse {
  asOf: string
  jobs: AdminJobSummary[]
  freshness: AdminFreshness[]
  contentCoverage: {
    descriptionsPublished: number
    descriptionsDraft: number
    universe: number
  }
  openQuarantineCount: number
}

/** One quarantined fact in the review queue (UXR-MDF-007/009). */
export interface QuarantineItem {
  id: number
  jobCode: string
  sourceRef: string | null
  reasonCode: string
  quarantinedAt: string
  payloadJson: unknown
  status: string
  resolutionNote: string | null
}

/** `GET /api/v1/admin/quarantine?status=open|dismissed` (`03` §8). */
export interface QuarantineListResponse {
  items: QuarantineItem[]
}

/** One data type's coverage entry in the coverage report (FR-MDF-011). */
export interface CoverageDataType {
  dataType: string
  present: boolean
  availableFrom: string | null
  availableTo: string | null
  notes: string | null
}

/** Instrument-scope coverage row (UXR-MDF-011). */
export interface InstrumentCoverageRow {
  symbol: string
  name: string
  listingDate: string | null
  dataTypes: CoverageDataType[]
}

/** Fund-scope coverage row (UXR-MDF-012). */
export interface FundCoverageRow {
  code: string
  name: string
  dataTypes: CoverageDataType[]
}

export type CoverageScope = 'instrument' | 'fund'

export interface InstrumentCoverageResponse {
  scope: 'instrument'
  instruments: InstrumentCoverageRow[]
}

export interface FundCoverageResponse {
  scope: 'fund'
  funds: FundCoverageRow[]
}

export type CoverageResponse = InstrumentCoverageResponse | FundCoverageResponse

/** One ingest run in the ledger (UXR-MDF-014). */
export interface IngestRun {
  id: number
  job: string
  status: string
  startedAt: string | null
  finishedAt: string | null
}

/** `GET /api/v1/admin/ingest-runs?job=&status=` (`03` §8). */
export interface IngestRunsResponse {
  runs: IngestRun[]
}

/** `POST /api/v1/admin/ingest/{job}/run` request body (`03` §8). */
export interface IngestTriggerRequest {
  backfillFrom?: string
}

/** `POST /api/v1/admin/ingest/{job}/run` response (`03` §8). */
export interface IngestTriggerResponse {
  runId: number | null
  job: string
  status: string
  written: number
  unchanged: number
  quarantined: number
}

/** `GET /api/v1/admin/stats` — aggregate-only counts (UXR-MDF-026). */
export interface AdminStatsResponse {
  accounts: { registered: number; verified: number }
  savedScreens: number
  dcfScenarios: number
}

/**
 * `POST /api/v1/admin/dcf-baselines/regenerate` response (`03` §8; UC-VAL-003,
 * UXR-MDF-025) — TKT-val-009. Mirrors the ingest-trigger result: the run that is
 * now visible in the ledger plus the counts the C3b regeneration job wrote.
 */
export interface DcfBaselineRegenerateResponse {
  runId: number | null
  job: string
  status: string
  written: number
  unchanged: number
  quarantined: number
}

/**
 * One catalog metric with its visibility state, as the SCR-012 metric-visibility
 * panel reads it (`GET /api/v1/admin/screener-metrics`; TKT-scr-005, UXR-MDF-023).
 * Unlike the public catalog this includes hidden rows, so the panel can toggle them
 * back on.
 */
export interface AdminMetricRow {
  metricCode: string
  family: string
  unit: string | null
  labelTr: string
  labelEn: string
  isScreenable: boolean
  isCagr: boolean
  sortOrder: number
}

/** `GET /api/v1/admin/screener-metrics` (`03` §8; TKT-scr-005). */
export interface AdminMetricsResponse {
  metrics: AdminMetricRow[]
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
