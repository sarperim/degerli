import { http, HttpResponse } from 'msw'

import {
  API_ERROR_STATUS,
  API_ERROR_TITLE,
  type ApiErrorCode,
  type MacroResponse,
  type ProblemDetails,
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
