import { http, HttpResponse } from 'msw'

import {
  API_ERROR_STATUS,
  API_ERROR_TITLE,
  type ApiErrorCode,
  type AuthenticatedSession,
  type AnonymousSession,
  type CsrfTokenResponse,
  type LoginResponse,
  type MacroResponse,
  type MetricCatalogResponse,
  type ProblemDetails,
  type RegisterResponse,
  type ResendVerificationResponse,
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
