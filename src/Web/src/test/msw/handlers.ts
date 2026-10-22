import { http, HttpResponse } from 'msw'

import {
  API_ERROR_STATUS,
  API_ERROR_TITLE,
  type AdminMetricsResponse,
  type AdminStatsResponse,
  type AdminSummaryResponse,
  type ApiErrorCode,
  type AuthenticatedSession,
  type AnonymousSession,
  type CoverageResponse,
  type CsrfTokenResponse,
  type DcfBaselineRegenerateResponse,
  type DcfBaselineResponse,
  type DcfComputeRequest,
  type DcfComputeResponse,
  type DcfSensitivity,
  type ForgotPasswordResponse,
  type LoginResponse,
  type IngestRun,
  type IngestRunsResponse,
  type IngestTriggerRequest,
  type IngestTriggerResponse,
  type MacroResponse,
  type MarketOverviewResponse,
  type MeResponse,
  type MetricCatalogResponse,
  type PasswordChangeResponse,
  type PriceState,
  type ProblemDetails,
  type QuarantineItem,
  type QuarantineListResponse,
  type RegisterResponse,
  type ResendVerificationResponse,
  type ResetPasswordResponse,
  type SavedScenario,
  type SavedScreen,
  type ScreenerRunResponse,
  type SessionResponse,
  type StockListItem,
} from '@/lib/api/contracts'

/** API prefix from `03-api-design.md` §1. */
export const API_BASE = '/api/v1'

/** Canonical problem `type` base (`03` §7). */
export const ERROR_TYPE_BASE = 'https://degerli.test/api/v1/errors'

type HttpMethod = 'get' | 'post' | 'put' | 'patch' | 'delete'

/** Deterministic fixture: a fresh macro envelope with both inflation measures. */
export const FRESH_MACRO_FIXTURE: MacroResponse = {
  asOf: '2026-10-06',
  stale: false,
  series: [
    {
      code: 'TUIK_CPI',
      value: 58.4,
      unit: '%',
      source: 'TUIK',
      asOf: '2026-10-03',
      stale: false,
      state: 'available',
    },
    {
      code: 'ENAG_CPI',
      value: 61.2,
      unit: '%',
      source: 'ENAG',
      asOf: '2026-10-03',
      stale: false,
      state: 'available',
    },
  ],
}

/** Deterministic fixture: a stale envelope with one unavailable series. */
export const STALE_MACRO_FIXTURE: MacroResponse = {
  asOf: '2026-10-06',
  stale: true,
  series: [
    {
      code: 'GOLD',
      value: 2450.75,
      unit: 'TRY/g',
      source: 'CBRT',
      asOf: '2026-09-26',
      stale: true,
      state: 'available',
    },
    {
      code: 'ENAG_CPI',
      value: null,
      unit: '%',
      source: 'ENAG',
      asOf: '2026-09-03',
      stale: true,
      state: 'unavailable',
    },
  ],
}

/**
 * The SCR-001 macro strip fixture (TKT-mov-006): the six V1 series in serving
 * order, every value available and fresh, typed against `MacroResponse`. Codes,
 * units and sources mirror the seeded `macro_series` definitions (FR-MOV-009/014).
 */
export const MACRO_STRIP_FIXTURE: MacroResponse = {
  asOf: '2026-10-06',
  stale: false,
  series: [
    { code: 'TUIK_CPI', value: 45.2, unit: '% YoY', source: 'TÜİK', asOf: '2026-09-01', stale: false, state: 'available' },
    { code: 'INDEP_CPI', value: 58.3, unit: '% YoY', source: 'ENAG', asOf: '2026-09-01', stale: false, state: 'available' },
    { code: 'CBRT_REPO', value: 42.5, unit: '%', source: 'TCMB', asOf: '2026-09-23', stale: false, state: 'available' },
    { code: 'USD_TRY', value: 47.1, unit: 'TRY', source: 'FX', asOf: '2026-10-06', stale: false, state: 'available' },
    { code: 'EUR_TRY', value: 51.4, unit: 'TRY', source: 'FX', asOf: '2026-10-06', stale: false, state: 'available' },
    { code: 'GOLD', value: 5200.0, unit: 'USD/oz', source: 'Gold', asOf: '2026-10-04', stale: false, state: 'available' },
  ],
}

/** The strip with a stale GOLD series (the always-seeded stale block, TC-MOV-024). */
export const MACRO_STRIP_STALE_FIXTURE: MacroResponse = {
  ...MACRO_STRIP_FIXTURE,
  stale: true,
  series: MACRO_STRIP_FIXTURE.series.map((series) =>
    series.code === 'GOLD' ? { ...series, stale: true } : series,
  ),
}

/** The degraded strip: the independent inflation measure is unavailable (TC-MOV-018). */
export const MACRO_STRIP_DEGRADED_FIXTURE: MacroResponse = {
  ...MACRO_STRIP_FIXTURE,
  series: MACRO_STRIP_FIXTURE.series.map((series) =>
    series.code === 'INDEP_CPI'
      ? { ...series, value: null, state: 'unavailable' as const }
      : series,
  ),
}

/** Convert an error code to the URL slug used in the problem `type`. */
export function errorTypeSlug(code: ApiErrorCode): string {
  return code.toLowerCase().replace(/_/g, '-')
}

/**
 * Build a typed RFC 7807 `application/problem+json` response from the `03` §7
 * error-code contract — status and title are derived from the code, never
 * hand-written per handler.
 */
export function problemResponse(
  code: ApiErrorCode,
  options: { params?: Record<string, unknown>; traceId?: string } = {},
): HttpResponse<ProblemDetails> {
  const status = API_ERROR_STATUS[code]
  const body: ProblemDetails = {
    type: `${ERROR_TYPE_BASE}/${errorTypeSlug(code)}`,
    title: API_ERROR_TITLE[code],
    status,
    code,
    ...(options.params ? { params: options.params } : {}),
    traceId: options.traceId ?? '00-test-0000000000000000-01',
  }

  return HttpResponse.json(body, {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

/** Register a request handler that always answers with the given problem. */
export function problemHandler(
  path: string,
  code: ApiErrorCode,
  options: {
    params?: Record<string, unknown>
    traceId?: string
    method?: HttpMethod
  } = {},
) {
  const resolve = () => problemResponse(code, options)

  switch (options.method ?? 'get') {
    case 'post':
      return http.post(path, resolve)
    case 'put':
      return http.put(path, resolve)
    case 'patch':
      return http.patch(path, resolve)
    case 'delete':
      return http.delete(path, resolve)
    case 'get':
    default:
      return http.get(path, resolve)
  }
}

/** `GET /api/v1/market/macro` handler, typed against `MacroResponse`. */
export function marketMacroHandler(body: MacroResponse = FRESH_MACRO_FIXTURE) {
  return http.get(`${API_BASE}/market/macro`, () => HttpResponse.json(body))
}

/**
 * Deterministic market-overview fixture derived from the FU §11.3 snapshot
 * goldens (indices, breadth, volume, market P/E + excluded count, dividend
 * yield, gainers/losers with volumes, cap-weighted sector performance).
 *
 * Percentage values are the **same fractions the API serves** (see
 * `MarketEndpointsTests`: `changePct` 0.02 / 0.10 / -0.10, sectors 0.00253 /
 * 0.022409 / 0.041726) — the Web double must never speak a different unit than
 * the server (TKT-foundation-036).
 */
export const MARKET_OVERVIEW_FIXTURE: MarketOverviewResponse = {
  asOf: '2026-10-06',
  stale: false,
  state: 'available',
  indices: {
    xu100: { level: 10200, changePct: 0.02 },
    xu30: { level: 30600, changePct: 0.02 },
  },
  breadth: { advancing: 10, declining: 5, unchanged: 1 },
  volumeTotal: 174_500_000,
  marketPe: 28.1218,
  marketPeExcludedCount: 2,
  marketDivYield: 0.011225,
  gainers: [
    { symbol: 'ETA', changePct: 0.1, volume: 2_000_000 },
    { symbol: 'NEWP', changePct: 0.04, volume: 1_500_000 },
    { symbol: 'ZETA', changePct: 0.03, volume: 1_200_000 },
    { symbol: 'KAPPA', changePct: 0.025, volume: 1_100_000 },
    { symbol: 'EPSL', changePct: 0.019888, volume: 1_050_000 },
    { symbol: 'PART', changePct: 0.015022, volume: 1_300_000 },
    { symbol: 'REST', changePct: 0.012151, volume: 1_400_000 },
    { symbol: 'ALFA', changePct: 0.010101, volume: 3_000_000 },
    { symbol: 'DELTA', changePct: 0.005025, volume: 1_600_000 },
  ],
  losers: [
    { symbol: 'THETA', changePct: -0.1, volume: 2_100_000 },
    { symbol: 'IOTA', changePct: -0.029126, volume: 1_700_000 },
    { symbol: 'BETA', changePct: -0.019608, volume: 1_800_000 },
    { symbol: 'GAMA', changePct: -0.012346, volume: 1_900_000 },
    { symbol: 'UNSEC', changePct: -0.004425, volume: 1_000_000 },
  ],
  sectorPerformance: { A: 0.00253, B: 0.022409, C: 0.041726 },
}

/**
 * `GET /api/v1/market/overview` handler. The block discriminator query param is
 * ignored (the endpoint always serves the whole snapshot), mirroring the server.
 */
export function marketOverviewHandler(
  body: MarketOverviewResponse = MARKET_OVERVIEW_FIXTURE,
) {
  return http.get(`${API_BASE}/market/overview`, () => HttpResponse.json(body))
}

/* ------------------------------------------------------------------ *
 * Intraday/close price-state doubles (`01` §10.9.x) — TKT-foundation-049.
 * ------------------------------------------------------------------ */

/**
 * Real-shape `priceState` doubles for the frozen contract (TKT-foundation-047
 * item 4) — the **instrument** (SCR-005) shape, which carries the full numeric
 * block. The instants are BIST session times expressed in UTC (Istanbul is
 * UTC+03): 12:15Z = 15:15 local, a sweep of the pinned cadence.
 *
 * `changePct` is the server's fraction **versus the previous session's close**
 * (10200) — the doubles never speak a different unit than the API (`03` §2), and
 * no assertion here derives it client-side.
 *
 * SCR-001's market-level shape is different and separate: `GET /market/overview`
 * serves `state`/`asOf`/`sessionDate` with the three numerics **null**
 * (TKT-foundation-047 `GetMarketStateAsync`; `01` §10.9.18) — see
 * `MARKET_PRICE_STATE_*` below.
 */
export const PRICE_STATE_INTRADAY: PriceState = {
  state: 'intraday',
  lastPrice: 10345.67,
  prevClose: 10200,
  changePct: 0.014281,
  asOf: '2026-10-22T12:15:00Z',
  sessionDate: '2026-10-22',
}

/**
 * The 18:00 close → EOD-landing window (planner-pinned in 047): the server keeps
 * serving `intraday` with its **last** `asOf` — here 18:05 Istanbul, i.e.
 * deliberately **after** the 18:00 close (this is a post-close "no freeze" proof,
 * not a scheduled sweep) — so the close in formation is never relabelled and the
 * UI must still say "last update … not final".
 */
export const PRICE_STATE_INTRADAY_AFTER_CLOSE: PriceState = {
  ...PRICE_STATE_INTRADAY,
  asOf: '2026-10-22T15:05:00Z',
}

/** After the EOD chain lands: the stored session close, dated by `sessionDate`. */
export const PRICE_STATE_CLOSE: PriceState = {
  state: 'close',
  lastPrice: 10412.5,
  prevClose: 10200,
  changePct: 0.020833,
  asOf: '2026-10-22T17:30:00Z',
  sessionDate: '2026-10-22',
}

/**
 * A deliberately inconsistent double (`lastPrice`/`prevClose` imply −50% while the
 * server says +50%): the UI must render the **served** `changePct`, proving it
 * never computes the change client-side (acceptance ii).
 */
export const PRICE_STATE_SERVER_CHANGE: PriceState = {
  ...PRICE_STATE_INTRADAY,
  lastPrice: 100,
  prevClose: 200,
  changePct: 0.5,
}

/**
 * An instrument's **cold start** (TKT-foundation-047 `GetInstrumentStateAsync`):
 * there is no previous stored close yet, so `prevClose`/`changePct` are `null`
 * while the served `lastPrice` is real. The UI must omit the change entirely —
 * never fabricate `+0,00%` (047: "never a fabricated 0").
 */
export const PRICE_STATE_COLD_START: PriceState = {
  ...PRICE_STATE_INTRADAY,
  prevClose: null,
  changePct: null,
}

/**
 * The **market-level** (SCR-001) shape `GET /market/overview` actually serves
 * (TKT-foundation-047 `GetMarketStateAsync`, `01` §10.9.18): the lane stores
 * instrument quotes only, so no index-level intraday level exists and the three
 * numerics are honestly `null`. The header must render only the state label —
 * no value, no change, no direction arrow, never `0,00` / `+0,00%`.
 */
export const MARKET_PRICE_STATE_INTRADAY: PriceState = {
  state: 'intraday',
  lastPrice: null,
  prevClose: null,
  changePct: null,
  asOf: '2026-10-22T12:15:00Z',
  sessionDate: '2026-10-22',
}

/** The market overview in the 18:00→EOD window: still `intraday`, last `asOf`. */
export const MARKET_PRICE_STATE_INTRADAY_AFTER_CLOSE: PriceState = {
  ...MARKET_PRICE_STATE_INTRADAY,
  asOf: '2026-10-22T15:05:00Z',
}

/** The market overview after the EOD chain: `close`, dated, still no numerics. */
export const MARKET_PRICE_STATE_CLOSE: PriceState = {
  ...MARKET_PRICE_STATE_INTRADAY,
  state: 'close',
  asOf: '2026-10-22T17:30:00Z',
}

/**
 * The fixture universe (FU §3) as the stock-list/typeahead endpoints serve it:
 * sector is the stable code (or `null` for the unclassified UNSEC).
 */
export const STOCK_LIST_FIXTURE: StockListItem[] = [
  { symbol: 'ALFA', name: 'Alfa Teknoloji A.Ş.', sector: 'A', listingDate: '2010-01-04' },
  { symbol: 'BETA', name: 'Beta Yazılım A.Ş.', sector: 'A', listingDate: '2012-05-10' },
  { symbol: 'GAMA', name: 'Gama Bilişim A.Ş.', sector: 'A', listingDate: '2015-09-01' },
  { symbol: 'DELTA', name: 'Delta Elektronik A.Ş.', sector: 'A', listingDate: '2011-03-15' },
  { symbol: 'REST', name: 'Rest Telekom A.Ş.', sector: 'A', listingDate: '2008-11-20' },
  { symbol: 'EPSL', name: 'Epsl Sanayi A.Ş.', sector: 'B', listingDate: '2009-04-01' },
  { symbol: 'ZETA', name: 'Zeta İmalat A.Ş.', sector: 'B', listingDate: '2024-03-15' },
  { symbol: 'ETA', name: 'Eta Enerji A.Ş.', sector: 'B', listingDate: '2013-06-03' },
  { symbol: 'THETA', name: 'Theta İnşaat A.Ş.', sector: 'B', listingDate: '2010-02-08' },
  { symbol: 'PART', name: 'Part Holding A.Ş.', sector: 'B', listingDate: '2005-08-15' },
  { symbol: 'NEWP', name: 'Yeni Gelişim A.Ş.', sector: 'B', listingDate: '2025-08-01' },
  { symbol: 'IOTA', name: 'Iota Gıda A.Ş.', sector: 'C', listingDate: '2014-10-09' },
  { symbol: 'KAPPA', name: 'Kappa İçecek A.Ş.', sector: 'C', listingDate: '2007-12-03' },
  { symbol: 'LAMDA', name: 'Lamda Tarım A.Ş.', sector: 'C', listingDate: '2016-04-12' },
  { symbol: 'NU', name: 'Nu Perakende A.Ş.', sector: 'C', listingDate: '2003-01-06' },
  { symbol: 'UNSEC', name: 'Unsur Ticaret A.Ş.', sector: null, listingDate: '2011-07-19' },
]

/** The stock-list envelope used by the fixture handlers. */
export const STOCK_LIST_ENVELOPE = { asOf: '2026-10-06', stale: false } as const

function matchesFilters(stock: StockListItem, sector: string | null, q: string | null) {
  const sectorMatch =
    !sector || (stock.sector ?? '').toLowerCase() === sector.toLowerCase()
  const queryMatch =
    !q ||
    stock.name.toLowerCase().includes(q.toLowerCase()) ||
    stock.symbol.toLowerCase().includes(q.toLowerCase())
  return sectorMatch && queryMatch
}

/**
 * `GET /api/v1/stocks?sector=&q=` handler mirroring the server semantics
 * (`03` §3; TC-RES-002/003): sector and query combine conjunctively, unknown
 * sector / non-match → empty list `200`.
 */
export function stockListHandler(stocks: StockListItem[] = STOCK_LIST_FIXTURE) {
  return http.get(`${API_BASE}/stocks`, ({ request }) => {
    const url = new URL(request.url)
    const sector = url.searchParams.get('sector')
    const q = url.searchParams.get('q')
    const filtered = stocks.filter((stock) => matchesFilters(stock, sector, q))
    return HttpResponse.json({ ...STOCK_LIST_ENVELOPE, stocks: filtered })
  })
}

/**
 * `GET /api/v1/stocks/search?q=` handler (FR-RES-004): symbol + name only; an
 * empty query yields no matches rather than dumping the universe.
 */
export function stockSearchHandler(stocks: StockListItem[] = STOCK_LIST_FIXTURE) {
  return http.get(`${API_BASE}/stocks/search`, ({ request }) => {
    const q = new URL(request.url).searchParams.get('q')
    const filtered = q && q.trim() ? stocks.filter((stock) => matchesFilters(stock, null, q)) : []
    return HttpResponse.json({
      ...STOCK_LIST_ENVELOPE,
      stocks: filtered.map(({ symbol, name }) => ({ symbol, name })),
    })
  })
}

/* ------------------------------------------------------------------ *
 * Identity handlers (`03` §6) — TKT-acc-007 (SCR-007 register + verify).
 * ------------------------------------------------------------------ */

/** Deterministic fixture: an anonymous session. */
export const ANONYMOUS_SESSION_FIXTURE: AnonymousSession = { authenticated: false }

/** Deterministic fixture: a signed-in but unverified account. */
export const UNVERIFIED_SESSION_FIXTURE: AuthenticatedSession = {
  authenticated: true,
  email: 'unverified@example.test',
  verified: false,
  languagePref: 'tr',
  role: 'user',
}

/** Deterministic fixture: a signed-in, verified account. */
export const VERIFIED_SESSION_FIXTURE: AuthenticatedSession = {
  authenticated: true,
  email: 'verified@example.test',
  verified: true,
  languagePref: 'tr',
  role: 'user',
}

/** `GET /api/v1/auth/session` handler, typed against `SessionResponse`. */
export function sessionHandler(body: SessionResponse = ANONYMOUS_SESSION_FIXTURE) {
  return http.get(`${API_BASE}/auth/session`, () => HttpResponse.json(body))
}

/** Deterministic antiforgery token fixture (`GET /api/v1/auth/csrf-token`, `03` §2). */
export const CSRF_TOKEN_FIXTURE: CsrfTokenResponse = {
  token: 'test-csrf-token',
  headerName: 'X-CSRF-Token',
}

/** `GET /api/v1/auth/csrf-token` handler — mirrors `Program.cs` `CsrfToken`. */
export function csrfTokenHandler() {
  return http.get(`${API_BASE}/auth/csrf-token`, () =>
    HttpResponse.json(CSRF_TOKEN_FIXTURE),
  )
}

/** `POST /api/v1/auth/register` handler — 201 `{email}` (`03` §6). */
export function registerHandler(
  body: RegisterResponse = { email: 'new-user@example.test' },
) {
  return http.post(`${API_BASE}/auth/register`, () =>
    HttpResponse.json(body, { status: 201 }),
  )
}

/** `POST /api/v1/auth/login` handler — 200 `{languagePref}` (`03` §6). */
export function loginHandler(body: LoginResponse = { languagePref: 'tr' }) {
  return http.post(`${API_BASE}/auth/login`, () => HttpResponse.json(body))
}

/** `POST /api/v1/auth/logout` handler — 204 no content (`03` §6). */
export function logoutHandler() {
  return http.post(`${API_BASE}/auth/logout`, () => new HttpResponse(null, { status: 204 }))
}

/** `POST /api/v1/auth/verify-email` handler — 200 verified. */
export function verifyEmailHandler() {
  return http.post(`${API_BASE}/auth/verify-email`, () =>
    HttpResponse.json({ verified: true }),
  )
}

/** `POST /api/v1/auth/resend-verification` handler — 200 `{verified}`. */
export function resendVerificationHandler(
  body: ResendVerificationResponse = { verified: false },
) {
  return http.post(`${API_BASE}/auth/resend-verification`, () =>
    HttpResponse.json(body),
  )
}

/** Deterministic fixture: the always-200 neutral reset-request confirmation. */
export const FORGOT_PASSWORD_FIXTURE: ForgotPasswordResponse = { status: 'accepted' }

/**
 * `POST /api/v1/auth/forgot-password` handler — always a neutral 200 (`03` §6).
 * The response carries no registration signal; the UI renders fixed copy.
 */
export function forgotPasswordHandler(
  body: ForgotPasswordResponse = FORGOT_PASSWORD_FIXTURE,
) {
  return http.post(`${API_BASE}/auth/forgot-password`, () =>
    HttpResponse.json(body),
  )
}

/** Deterministic fixture: reset-password 200 `{status:"reset"}`. */
export const RESET_PASSWORD_FIXTURE: ResetPasswordResponse = { status: 'reset' }

/** `POST /api/v1/auth/reset-password` handler — 200 (may establish a session). */
export function resetPasswordHandler(
  body: ResetPasswordResponse = RESET_PASSWORD_FIXTURE,
) {
  return http.post(`${API_BASE}/auth/reset-password`, () =>
    HttpResponse.json(body),
  )
}

/* ------------------------------------------------------------------ *
 * Account settings (`03` §6) — TKT-acc-010 (SCR-010).
 * ------------------------------------------------------------------ */

/** Deterministic fixture: the minimal account read for a verified account. */
export const ME_VERIFIED_FIXTURE: MeResponse = {
  email: 'verified@example.test',
  languagePref: 'tr',
  verified: true,
}

/** Deterministic fixture: the minimal account read for an unverified account. */
export const ME_UNVERIFIED_FIXTURE: MeResponse = {
  email: 'unverified@example.test',
  languagePref: 'tr',
  verified: false,
}

/** `GET /api/v1/me` handler — the SCR-010 settings data (`03` §6). */
export function meHandler(body: MeResponse = ME_VERIFIED_FIXTURE) {
  return http.get(`${API_BASE}/me`, () => HttpResponse.json(body))
}

/** `PATCH /api/v1/me` handler — 200 with the updated account read. */
export function updateLanguageHandler(body: MeResponse = ME_VERIFIED_FIXTURE) {
  return http.patch(`${API_BASE}/me`, () => HttpResponse.json(body))
}

/** `PATCH /api/v1/me/password` handler — 200 `{email}` (session preserved). */
export function passwordChangeHandler(
  body: PasswordChangeResponse = { email: ME_VERIFIED_FIXTURE.email },
) {
  return http.patch(`${API_BASE}/me/password`, () => HttpResponse.json(body))
}

/** `DELETE /api/v1/me` handler — 204 (hard delete + cascade; session revoked). */
export function deleteAccountHandler() {
  return http.delete(`${API_BASE}/me`, () => new HttpResponse(null, { status: 204 }))
}

/* ------------------------------------------------------------------ *
 * Screener handlers (`03` §4) — TKT-scr-006 (SCR-003).
 * ------------------------------------------------------------------ */

/**
 * The 18-metric visible catalog as `GET /api/v1/screener/metrics` serves it,
 * mirroring the seed (`DegerliSeed.MetricCatalogSql`): five families in catalog
 * sort order; `%` metrics carry fractional values, ratio metrics `x`.
 */
export const METRIC_CATALOG_FIXTURE: MetricCatalogResponse = {
  families: [
    {
      family: 'valuation',
      metrics: [
        { metricCode: 'pe', unit: 'x', labelTr: 'F/K', labelEn: 'P/E', isCagr: false, sortOrder: 1 },
        { metricCode: 'pb', unit: 'x', labelTr: 'PD/DD', labelEn: 'P/B', isCagr: false, sortOrder: 2 },
        { metricCode: 'ev_ebitda', unit: 'x', labelTr: 'FD/FAVÖK', labelEn: 'EV/EBITDA', isCagr: false, sortOrder: 3 },
        { metricCode: 'ev_fcf', unit: 'x', labelTr: 'FD/Serbest Nakit Akışı', labelEn: 'EV/FCF', isCagr: false, sortOrder: 4 },
        { metricCode: 'fcf_yield', unit: '%', labelTr: 'Serbest Nakit Akışı Getirisi', labelEn: 'FCF Yield', isCagr: false, sortOrder: 5 },
      ],
    },
    {
      family: 'quality',
      metrics: [
        { metricCode: 'roic', unit: '%', labelTr: 'Yatırılan Sermaye Getirisi', labelEn: 'ROIC', isCagr: false, sortOrder: 6 },
        { metricCode: 'roe', unit: '%', labelTr: 'Özkaynak Kârlılığı', labelEn: 'ROE', isCagr: false, sortOrder: 7 },
        { metricCode: 'gross_margin', unit: '%', labelTr: 'Brüt Kâr Marjı', labelEn: 'Gross Margin', isCagr: false, sortOrder: 8 },
        { metricCode: 'operating_margin', unit: '%', labelTr: 'Faaliyet Kâr Marjı', labelEn: 'Operating Margin', isCagr: false, sortOrder: 9 },
      ],
    },
    {
      family: 'growth',
      metrics: [
        { metricCode: 'rev_cagr', unit: '%', labelTr: 'Hasılat Bileşik Büyüme', labelEn: 'Revenue CAGR', isCagr: true, sortOrder: 10 },
        { metricCode: 'eps_cagr', unit: '%', labelTr: 'HBK Bileşik Büyüme', labelEn: 'EPS CAGR', isCagr: true, sortOrder: 11 },
        { metricCode: 'fcf_cagr', unit: '%', labelTr: 'Serbest Nakit Akışı Bileşik Büyüme', labelEn: 'FCF CAGR', isCagr: true, sortOrder: 12 },
      ],
    },
    {
      family: 'financial_health',
      metrics: [
        { metricCode: 'net_debt_ebitda', unit: 'x', labelTr: 'Net Borç/FAVÖK', labelEn: 'Net Debt/EBITDA', isCagr: false, sortOrder: 13 },
        { metricCode: 'interest_coverage', unit: 'x', labelTr: 'Faiz Karşılama Oranı', labelEn: 'Interest Coverage', isCagr: false, sortOrder: 14 },
        { metricCode: 'current_ratio', unit: 'x', labelTr: 'Cari Oran', labelEn: 'Current Ratio', isCagr: false, sortOrder: 15 },
      ],
    },
    {
      family: 'dividends',
      metrics: [
        { metricCode: 'div_yield', unit: '%', labelTr: 'Temettü Verimi', labelEn: 'Dividend Yield', isCagr: false, sortOrder: 16 },
        { metricCode: 'div_cagr', unit: '%', labelTr: 'Temettü Bileşik Büyüme', labelEn: 'Dividend CAGR', isCagr: true, sortOrder: 17 },
        { metricCode: 'payout_ratio', unit: '%', labelTr: 'Dağıtım Oranı', labelEn: 'Payout Ratio', isCagr: false, sortOrder: 18 },
      ],
    },
  ],
}

/** `GET /api/v1/screener/metrics` handler. */
export function screenerMetricsHandler(body: MetricCatalogResponse = METRIC_CATALOG_FIXTURE) {
  return http.get(`${API_BASE}/screener/metrics`, () => HttpResponse.json(body))
}

/**
 * The run response for `pe ≤ 15 AND roe ≥ 15%` (TC-SCR-002/026 golden):
 * ALFA/REST/UNSEC match; BETA/GAMA are excluded for missing P/E.
 */
export const SCREENER_RUN_FIXTURE: ScreenerRunResponse = {
  asOf: '2026-10-06',
  stale: false,
  matchCount: 3,
  excludedCount: 2,
  droppedCriteria: [],
  rows: [
    { symbol: 'ALFA', name: 'Alfa Teknoloji A.Ş.', sector: 'A', values: { pe: 10.0, roe: 0.2 } },
    { symbol: 'REST', name: 'Rest Telekom A.Ş.', sector: 'A', values: { pe: 10.0, roe: 0.15 } },
    { symbol: 'UNSEC', name: 'Unsur Ticaret A.Ş.', sector: null, values: { pe: 14.0, roe: 0.2143 } },
  ],
}

/** `POST /api/v1/screener/run` handler returning a fixed response. */
export function screenerRunHandler(body: ScreenerRunResponse = SCREENER_RUN_FIXTURE) {
  return http.post(`${API_BASE}/screener/run`, () => HttpResponse.json(body))
}

/** A saved-screen row as `POST /me/screens` serves it (TC-SCR-012). */
export const SAVED_SCREEN_FIXTURE: SavedScreen = {
  id: 1,
  name: 'Ucuz kaliteli',
  criteria: [
    { metricCode: 'pe', bound: 'max', maxValue: 15 },
    { metricCode: 'roe', bound: 'min', minValue: 0.15 },
  ],
  createdAt: '2026-10-06T20:00:00Z',
  updatedAt: '2026-10-06T20:00:00Z',
}

/** `POST /api/v1/me/screens` handler — 201 with the created screen. */
export function saveScreenHandler(body: SavedScreen = SAVED_SCREEN_FIXTURE) {
  return http.post(`${API_BASE}/me/screens`, () => HttpResponse.json(body, { status: 201 }))
}

/* ------------------------------------------------------------------ *
 * Admin / SCR-012 handlers (`03` §8) — TKT-mdf-012 (MDF sections).
 * ------------------------------------------------------------------ */

/** Deterministic fixture: the signed-in builder account (SCR-012 gate). */
export const BUILDER_SESSION_FIXTURE: AuthenticatedSession = {
  authenticated: true,
  email: 'builder@degerli.test',
  verified: true,
  languagePref: 'tr',
  role: 'builder',
}

/**
 * The ops-summary fixture (TC-MDF-046): two jobs, a stale `gold` data type with
 * its last-success date, description coverage counts and an open-quarantine count.
 */
export const ADMIN_SUMMARY_FIXTURE: AdminSummaryResponse = {
  asOf: '2026-10-06',
  jobs: [
    { job: 'prices', lastRun: { status: 'succeeded', finishedAt: '2026-10-06T21:14:03Z' } },
    { job: 'statements', lastRun: { status: 'failed', finishedAt: '2026-10-06T21:15:11Z' } },
  ],
  freshness: [
    { dataType: 'prices', lastSuccess: '2026-10-06', stale: false },
    { dataType: 'gold', lastSuccess: '2026-09-26', stale: true },
  ],
  contentCoverage: { descriptionsPublished: 61, descriptionsDraft: 12, universe: 100 },
  openQuarantineCount: 4,
}

/**
 * `GET /api/v1/admin/summary` handler. Accepts a static body or a thunk (so a
 * test can make the summary reflect a mutation — e.g. a dismissal decrementing
 * the open count, TC-MDF-047 — between reads).
 */
export function adminSummaryHandler(
  body: AdminSummaryResponse | (() => AdminSummaryResponse) = ADMIN_SUMMARY_FIXTURE,
) {
  return http.get(`${API_BASE}/admin/summary`, () =>
    HttpResponse.json(typeof body === 'function' ? body() : body),
  )
}

/** Two quarantine rows: one open, one already dismissed with its note. */
export const QUARANTINE_FIXTURE: QuarantineItem[] = [
  {
    id: 1,
    jobCode: 'prices',
    sourceRef: 'prices-ok.json#ALFA',
    reasonCode: 'NEGATIVE_PRICE',
    quarantinedAt: '2026-10-06T21:14:03Z',
    payloadJson: { symbol: 'ALFA', close: -5 },
    status: 'open',
    resolutionNote: null,
  },
  {
    id: 2,
    jobCode: 'statements',
    sourceRef: 'statements-ok.json#BETA',
    reasonCode: 'SCHEMA_MISMATCH',
    quarantinedAt: '2026-10-05T21:10:00Z',
    payloadJson: { symbol: 'BETA', value: 'n/a' },
    status: 'dismissed',
    resolutionNote: 'source error accepted',
  },
]

/* ------------------------------------------------------------------ *
 * Valuation & DCF handlers (`03` §5) — TKT-val-006 (SCR-006 calculator).
 * ------------------------------------------------------------------ */

/** Canonical price fixture — the same `daily_prices` row every surface serves. */
export const DCF_PRICE_FIXTURE = {
  value: 20.0,
  asOf: '2026-10-06',
  stale: false,
} as const

/**
 * The ALFA baseline (FU §11.5): the eight defaults, canonical-fact provenance for
 * the four statement-derived inputs, and the canonical price.
 */
export const DCF_BASELINE_FIXTURE: DcfBaselineResponse = {
  symbol: 'ALFA',
  baseline: {
    version: 1,
    buildDate: '2026-10-05',
    params: {
      base_fcf: 100,
      growth_rate: 0.1,
      horizon_years: 5,
      terminal_growth: 0.03,
      discount_rate: 0.12,
      debt: 400,
      cash: 200,
      share_count: 100,
    },
    canonicalFactRefs: {
      base_fcf: { asOf: '2026-09-30', restated: false },
      debt: { asOf: '2026-09-30', restated: false },
      cash: { asOf: '2026-09-30', restated: false },
      share_count: { asOf: '2026-09-30', restated: false },
    },
  },
  price: { ...DCF_PRICE_FIXTURE },
}

/** The 5×5 golden sensitivity grid (TC-VAL-002), centered on the ALFA baseline. */
export const DCF_SENSITIVITY_FIXTURE: DcfSensitivity = {
  discountRates: [0.1, 0.11, 0.12, 0.13, 0.14],
  terminalGrowths: [0.01, 0.02, 0.03, 0.04, 0.05],
  fairValues: [
    [18.5, 20.4, 22.1, 23.9, 24.0],
    [15.9, 17.2, 18.6, 19.9, 21.2],
    [13.6, 14.4, 13.1969, 15.1, 16.5],
    [11.8, 12.4, 13.0, 13.9, 14.8],
    [8.99623, 10.2, 10.9, 11.6, 12.3],
  ],
}

/** The ALFA compute golden (TC-VAL-001/009): FVPS 13.1969, MoS −0.5155. */
export const DCF_COMPUTE_FIXTURE: DcfComputeResponse = {
  fairValuePerShare: 13.1969,
  marginOfSafety: -0.5155,
  price: { ...DCF_PRICE_FIXTURE },
  sensitivity: DCF_SENSITIVITY_FIXTURE,
}

/** `GET /api/v1/stocks/{symbol}/dcf` handler. */
export function dcfBaselineHandler(body: DcfBaselineResponse = DCF_BASELINE_FIXTURE) {
  return http.get(`${API_BASE}/stocks/:symbol/dcf`, () => HttpResponse.json(body))
}

/**
 * `POST /api/v1/stocks/{symbol}/dcf/compute` handler. The optional `respond`
 * callback lets a test derive the response from the posted parameters (mirroring
 * the stateless server); it defaults to the ALFA golden.
 */
export function dcfComputeHandler(
  respond: (body: DcfComputeRequest) => DcfComputeResponse = () => DCF_COMPUTE_FIXTURE,
) {
  return http.post(`${API_BASE}/stocks/:symbol/dcf/compute`, async ({ request }) => {
    const body = (await request.json()) as DcfComputeRequest
    return HttpResponse.json(respond(body))
  })
}

/* ------------------------------------------------------------------ *
 * Saved screens (`03` §4) — TKT-scr-007 (SCR-004 list/rename/delete).
 * ------------------------------------------------------------------ */

/** Two saved screens as `GET /api/v1/me/screens` serves them (by name). */
export const SAVED_SCREENS_FIXTURE: SavedScreen[] = [
  {
    id: 1,
    name: 'Ucuz kaliteli',
    criteria: [
      { metricCode: 'pe', bound: 'max', maxValue: 15 },
      { metricCode: 'roe', bound: 'min', minValue: 0.15 },
    ],
    createdAt: '2026-10-05T20:00:00Z',
    updatedAt: '2026-10-05T20:00:00Z',
  },
  {
    id: 2,
    name: 'Büyüme',
    criteria: [{ metricCode: 'rev_cagr', bound: 'min', minValue: 0.15, window: 5 }],
    createdAt: '2026-10-06T20:00:00Z',
    updatedAt: '2026-10-06T20:00:00Z',
  },
]

/**
 * Stateful quarantine handlers: the list honours `?status=`; a dismissal moves
 * the item to `dismissed` and records the note, so the follow-up read reflects it
 * without a reload (TC-MDF-047).
 */
export function quarantineHandlers(
  initial: QuarantineItem[] = QUARANTINE_FIXTURE,
  options: { onDismiss?: (item: QuarantineItem) => void } = {},
) {
  let items = initial.map((item) => ({ ...item }))

  const list = http.get(`${API_BASE}/admin/quarantine`, ({ request }) => {
    const status = new URL(request.url).searchParams.get('status')
    const body: QuarantineListResponse = {
      items: status ? items.filter((item) => item.status === status) : items,
    }
    return HttpResponse.json(body)
  })

  const dismiss = http.post(
    `${API_BASE}/admin/quarantine/:id/dismiss`,
    async ({ request, params }) => {
      const body = (await request.json().catch(() => ({}))) as { note?: string }
      const id = Number(params.id)
      const existing = items.find((item) => item.id === id)
      if (!existing) return problemResponse('NOT_FOUND')
      if (existing.status === 'open') {
        const updated: QuarantineItem = {
          ...existing,
          status: 'dismissed',
          resolutionNote: body.note ?? null,
        }
        items = items.map((item) => (item.id === id ? updated : item))
        options.onDismiss?.(updated)
        return HttpResponse.json(updated)
      }
      return HttpResponse.json(existing)
    },
  )

  return {
    list,
    dismiss,
    handlers: [list, dismiss],
    get items() {
      return items
    },
  }
}

/** Instrument-scope coverage fixture: PART carries an explicit statements gap. */
export const INSTRUMENT_COVERAGE_FIXTURE: CoverageResponse = {
  scope: 'instrument',
  instruments: [
    {
      symbol: 'ALFA',
      name: 'Alfa Teknoloji A.Ş.',
      listingDate: '2010-01-04',
      dataTypes: [
        { dataType: 'prices', present: true, availableFrom: '2016-01-04', availableTo: '2026-10-06', notes: null },
        { dataType: 'statements', present: true, availableFrom: '2016-01-04', availableTo: '2026-10-06', notes: null },
      ],
    },
    {
      symbol: 'PART',
      name: 'Part Holding A.Ş.',
      listingDate: '2005-08-15',
      dataTypes: [
        { dataType: 'prices', present: true, availableFrom: '2016-01-04', availableTo: '2026-10-06', notes: null },
        { dataType: 'statements', present: false, availableFrom: null, availableTo: null, notes: 'cash-flow statement missing' },
      ],
    },
  ],
}

/** Fund-scope coverage fixture: TEF0002's holdings are an explicit gap. */
export const FUND_COVERAGE_FIXTURE: CoverageResponse = {
  scope: 'fund',
  funds: [
    {
      code: 'TEF0001',
      name: 'Örnek Hisse Fonu',
      dataTypes: [
        { dataType: 'nav', present: true, availableFrom: '2026-09-28', availableTo: '2026-10-06', notes: null },
        { dataType: 'performance', present: true, availableFrom: '2026-09-28', availableTo: '2026-10-06', notes: null },
        { dataType: 'holdings', present: true, availableFrom: '2026-09-28', availableTo: '2026-10-06', notes: null },
      ],
    },
    {
      code: 'TEF0002',
      name: 'Örnek Karma Fon',
      dataTypes: [
        { dataType: 'nav', present: true, availableFrom: '2026-09-28', availableTo: '2026-10-06', notes: null },
        { dataType: 'performance', present: true, availableFrom: '2026-09-28', availableTo: '2026-10-06', notes: null },
        { dataType: 'holdings', present: false, availableFrom: null, availableTo: null, notes: 'holdings not published' },
      ],
    },
    {
      code: 'TEF0003',
      name: 'Örnek Serbest Fon',
      dataTypes: [
        { dataType: 'nav', present: true, availableFrom: '2026-09-28', availableTo: '2026-10-06', notes: null },
        { dataType: 'holdings', present: true, availableFrom: '2026-09-28', availableTo: null, notes: 'partial' },
      ],
    },
  ],
}

/** `GET /api/v1/admin/coverage?scope=` handler selecting by the scope param. */
export function adminCoverageHandler(
  byScope: { instrument?: CoverageResponse; fund?: CoverageResponse } = {
    instrument: INSTRUMENT_COVERAGE_FIXTURE,
    fund: FUND_COVERAGE_FIXTURE,
  },
) {
  return http.get(`${API_BASE}/admin/coverage`, ({ request }) => {
    const scope = new URL(request.url).searchParams.get('scope') ?? 'instrument'
    const body = scope === 'fund' ? byScope.fund : byScope.instrument
    return HttpResponse.json(body ?? byScope.instrument)
  })
}

/** One succeeded and one still-running run (the auto-refresh case, TC-MDF-048). */
export const INGEST_RUNS_FIXTURE: IngestRun[] = [
  { id: 11, job: 'prices', status: 'running', startedAt: '2026-10-06T21:14:00Z', finishedAt: null },
  { id: 10, job: 'statements', status: 'succeeded', startedAt: '2026-10-05T20:30:00Z', finishedAt: '2026-10-05T20:31:10Z' },
]

/**
 * Stateful ingest-run handlers: the list honours the conjunctive job/status
 * filters (TC-MDF-041/048); `addRun` lets a test make a triggered run appear.
 */
export function ingestRunsHandlers(initial: IngestRun[] = INGEST_RUNS_FIXTURE) {
  let runs = initial.map((run) => ({ ...run }))

  const list = http.get(`${API_BASE}/admin/ingest-runs`, ({ request }) => {
    const url = new URL(request.url)
    const job = url.searchParams.get('job')
    const status = url.searchParams.get('status')
    const body: IngestRunsResponse = {
      runs: runs.filter(
        (run) => (!job || run.job === job) && (!status || run.status === status),
      ),
    }
    return HttpResponse.json(body)
  })

  return {
    list,
    handlers: [list],
    get runs() {
      return runs
    },
    addRun(run: IngestRun) {
      runs = [run, ...runs]
    },
  }
}

/** `POST /api/v1/admin/ingest/{job}/run` handler recording each trigger. */
export function ingestTriggerHandler(
  onTrigger?: (job: string, body: IngestTriggerRequest) => void,
  response: IngestTriggerResponse = {
    runId: 12,
    job: 'prices',
    status: 'running',
    written: 0,
    unchanged: 0,
    quarantined: 0,
  },
) {
  return http.post(`${API_BASE}/admin/ingest/:job/run`, async ({ request, params }) => {
    const body = (await request.json().catch(() => ({}))) as IngestTriggerRequest
    onTrigger?.(String(params.job), body)
    return HttpResponse.json({ ...response, job: String(params.job) })
  })
}

/** `GET /api/v1/admin/stats` fixture — aggregate only (UXR-MDF-026). */
export const ADMIN_STATS_FIXTURE: AdminStatsResponse = {
  accounts: { registered: 12, verified: 9 },
  savedScreens: 7,
  dcfScenarios: 3,
}

/** `GET /api/v1/admin/stats` handler. */
export function adminStatsHandler(body: AdminStatsResponse = ADMIN_STATS_FIXTURE) {
  return http.get(`${API_BASE}/admin/stats`, () => HttpResponse.json(body))
}

/**
 * `POST /api/v1/admin/dcf-baselines/regenerate` fixture — the C3b regeneration
 * run result (TKT-val-009); `runId` lets the UI point at the ledger row.
 */
export const DCF_BASELINE_REGENERATE_FIXTURE: DcfBaselineRegenerateResponse = {
  runId: 21,
  job: 'dcf-baselines',
  status: 'succeeded',
  written: 16,
  unchanged: 0,
  quarantined: 0,
}

/**
 * `POST /api/v1/admin/dcf-baselines/regenerate` handler recording each call (so a
 * test can prove cancel aborts with no run). Accepts a static body or a thunk.
 */
export function dcfBaselineRegenerateHandler(
  onRegenerate?: () => void,
  body: DcfBaselineRegenerateResponse | (() => DcfBaselineRegenerateResponse) =
    DCF_BASELINE_REGENERATE_FIXTURE,
) {
  return http.post(`${API_BASE}/admin/dcf-baselines/regenerate`, () => {
    onRegenerate?.()
    return HttpResponse.json(typeof body === 'function' ? body() : body)
  })
}

/**
 * The full screener catalog the visibility panel reads — all 18 metrics, flattened
 * from the visible catalog with a `screenable` default (TKT-scr-005; UXR-MDF-023).
 */
export const ADMIN_METRICS_FIXTURE: AdminMetricsResponse = {
  metrics: METRIC_CATALOG_FIXTURE.families.flatMap((family) =>
    family.metrics.map((metric) => ({
      metricCode: metric.metricCode,
      family: family.family,
      unit: metric.unit,
      labelTr: metric.labelTr,
      labelEn: metric.labelEn,
      isScreenable: true,
      isCagr: metric.isCagr,
      sortOrder: metric.sortOrder,
    })),
  ),
}

/** `GET /api/v1/admin/screener-metrics` handler (static body). */
export function adminMetricsHandler(body: AdminMetricsResponse = ADMIN_METRICS_FIXTURE) {
  return http.get(`${API_BASE}/admin/screener-metrics`, () => HttpResponse.json(body))
}

/**
 * Stateful metric-visibility handlers: a PATCH flips the row and later GETs reflect
 * it, so a toggle's effect on the panel is observable (TC-SCR-021).
 */
export function adminMetricVisibilityHandlers(
  initial: AdminMetricsResponse = ADMIN_METRICS_FIXTURE,
) {
  let metrics = initial.metrics.map((metric) => ({ ...metric }))

  const list = http.get(`${API_BASE}/admin/screener-metrics`, () =>
    HttpResponse.json({ metrics } satisfies AdminMetricsResponse),
  )

  const patch = http.patch(
    `${API_BASE}/admin/screener-metrics/:code`,
    async ({ request, params }) => {
      const body = (await request.json().catch(() => ({}))) as { isScreenable?: boolean }
      const code = String(params.code)
      metrics = metrics.map((metric) =>
        metric.metricCode === code
          ? { ...metric, isScreenable: body.isScreenable ?? metric.isScreenable }
          : metric,
      )
      const updated = metrics.find((metric) => metric.metricCode === code)
      return updated ? HttpResponse.json(updated) : new HttpResponse(null, { status: 404 })
    },
  )

  return {
    handlers: [list, patch],
    get metrics() {
      return metrics
    },
  }
}

/**
 * `GET /api/v1/me/screens` handler. Accepts a static list or a thunk (so a test
 * can mutate the "server" list between renders — TC-SCR-031).
 */
export function savedScreensListHandler(
  screens: SavedScreen[] | (() => SavedScreen[]) = SAVED_SCREENS_FIXTURE,
) {
  return http.get(`${API_BASE}/me/screens`, () =>
    HttpResponse.json({ screens: typeof screens === 'function' ? screens() : screens }),
  )
}

/** `PATCH /api/v1/me/screens/{id}` handler — 200 with the updated screen. */
export function renameScreenHandler(body: SavedScreen) {
  return http.patch(`${API_BASE}/me/screens/:id`, () => HttpResponse.json(body))
}

/** `DELETE /api/v1/me/screens/{id}` handler — 204 (TC-SCR-017). */
export function deleteScreenHandler() {
  return http.delete(`${API_BASE}/me/screens/:id`, () => new HttpResponse(null, { status: 204 }))
}

/* ------------------------------------------------------------------ *
 * DCF scenarios (`03` §5) — TKT-val-007 (SCR-006 save/load + picker).
 * ------------------------------------------------------------------ */

/**
 * A saved ALFA scenario with a single parameter override (FR-VAL-010): every
 * other value mirrors the baseline, so a load that restores it exactly is
 * distinguishable from the baseline by the visible 15% discount rate.
 */
export const DCF_SCENARIO_ALFA: SavedScenario = {
  id: 1,
  symbol: 'ALFA',
  name: 'Muhafazakâr',
  params: { ...DCF_BASELINE_FIXTURE.baseline!.params, discount_rate: 0.15 },
  createdAt: '2026-10-06T20:00:00Z',
  updatedAt: '2026-10-06T20:00:00Z',
}

/** A second saved scenario for a different stock (SCR-011 grouping, TKT-val-008). */
export const DCF_SCENARIO_BETA: SavedScenario = {
  id: 2,
  symbol: 'BETA',
  name: 'Beta temel',
  params: { ...DCF_BASELINE_FIXTURE.baseline!.params, discount_rate: 0.1 },
  createdAt: '2026-10-06T20:30:00Z',
  updatedAt: '2026-10-06T20:30:00Z',
}

/**
 * `GET /api/v1/me/scenarios?symbol=` handler. Accepts a static list or a thunk
 * (so a test can mutate the "server" list between renders — mutation matrix §8.4
 * picker invalidation). Filters by the `symbol` query parameter when present.
 */
export function scenariosListHandler(
  scenarios: SavedScenario[] | (() => SavedScenario[]) = [DCF_SCENARIO_ALFA],
) {
  return http.get(`${API_BASE}/me/scenarios`, ({ request }) => {
    const symbol = new URL(request.url).searchParams.get('symbol')
    const all = typeof scenarios === 'function' ? scenarios() : scenarios
    const filtered = symbol
      ? all.filter((scenario) => scenario.symbol === symbol.toUpperCase())
      : all
    return HttpResponse.json({ scenarios: filtered })
  })
}

/** `PATCH /api/v1/me/scenarios/{id}` handler — 200 with the updated scenario. */
export function renameScenarioHandler(body: SavedScenario = DCF_SCENARIO_ALFA) {
  return http.patch(`${API_BASE}/me/scenarios/:id`, () => HttpResponse.json(body))
}

/** `DELETE /api/v1/me/scenarios/{id}` handler — 204 (UXR-VAL-018). */
export function deleteScenarioHandler() {
  return http.delete(`${API_BASE}/me/scenarios/:id`, () => new HttpResponse(null, { status: 204 }))
}
