import { apiGet } from '@/lib/api/client'
import type {
  MissingDataResponse,
  StockBalanceSheetResponse,
  StockDividendsResponse,
  StockFinancialsResponse,
  StockGrowthResponse,
  StockKeyMetricsResponse,
  StockOverviewResponse,
  StockProfitabilityResponse,
  StockValuationResponse,
} from '@/lib/api/contracts'

/**
 * SCR-005 section data access (`03-api-design.md` §3; FR-RES-005..012/014..016/027).
 *
 * Each of the stock page's sections is a separate endpoint (M-5): they load,
 * fail and retry independently, so one slow or failing section never blocks the
 * others. The server does all computation (BR-RES-004); these functions only
 * fetch, and the client renders from the returned values.
 */

/** Sections with their own endpoint. */
export type StockSectionEndpoint =
  | 'overview'
  | 'valuation'
  | 'financials'
  | 'profitability'
  | 'growth'
  | 'balanceSheet'
  | 'dividends'
  | 'keyMetrics'

/** A section payload: the data shape, or the explicit missing-data envelope. */
export type SectionResponse<T> = T | MissingDataResponse

/** Query-key factory — one place so per-section caching/invalidation stays consistent. */
export const stockPageQueryKeys = {
  all: (symbol: string) => ['stocks', symbol] as const,
  overview: (symbol: string) => ['stocks', symbol, 'overview'] as const,
  valuation: (symbol: string) => ['stocks', symbol, 'valuation'] as const,
  financials: (symbol: string) => ['stocks', symbol, 'financials'] as const,
  profitability: (symbol: string) => ['stocks', symbol, 'profitability'] as const,
  growth: (symbol: string) => ['stocks', symbol, 'growth'] as const,
  balanceSheet: (symbol: string) => ['stocks', symbol, 'balance-sheet'] as const,
  dividends: (symbol: string) => ['stocks', symbol, 'dividends'] as const,
  keyMetrics: (symbol: string) => ['stocks', symbol, 'key-metrics'] as const,
}

function sectionPath(symbol: string, section: string): string {
  return `/stocks/${encodeURIComponent(symbol)}/${section}`
}

/** `GET /api/v1/stocks/{symbol}/overview` — business description (UC-RES-002). */
export function fetchStockOverview(
  symbol: string,
  signal?: AbortSignal,
): Promise<StockOverviewResponse> {
  return apiGet<StockOverviewResponse>(sectionPath(symbol, 'overview'), signal)
}

/**
 * `GET /api/v1/stocks/{symbol}/valuation` — current multiples, the adjusted-basis
 * historical series and the vs.-sector strip (FR-RES-006/007). The endpoint always
 * serves the full payload (its honesty lives per-metric in the strip), so there is
 * no section-level missing-data branch here.
 */
export function fetchStockValuation(
  symbol: string,
  signal?: AbortSignal,
): Promise<StockValuationResponse> {
  return apiGet<StockValuationResponse>(sectionPath(symbol, 'valuation'), signal)
}

/** `GET /api/v1/stocks/{symbol}/financials` — per-period statements (FR-RES-008). */
export function fetchStockFinancials(
  symbol: string,
  signal?: AbortSignal,
): Promise<SectionResponse<StockFinancialsResponse>> {
  return apiGet<SectionResponse<StockFinancialsResponse>>(
    sectionPath(symbol, 'financials'),
    signal,
  )
}

/** `GET /api/v1/stocks/{symbol}/profitability` — ROIC/ROE/margins (FR-RES-009). */
export function fetchStockProfitability(
  symbol: string,
  signal?: AbortSignal,
): Promise<SectionResponse<StockProfitabilityResponse>> {
  return apiGet<SectionResponse<StockProfitabilityResponse>>(
    sectionPath(symbol, 'profitability'),
    signal,
  )
}

/** `GET /api/v1/stocks/{symbol}/growth` — 3/5/10Y CAGRs + actual window (FR-RES-010/016). */
export function fetchStockGrowth(
  symbol: string,
  signal?: AbortSignal,
): Promise<SectionResponse<StockGrowthResponse>> {
  return apiGet<SectionResponse<StockGrowthResponse>>(
    sectionPath(symbol, 'growth'),
    signal,
  )
}

/** `GET /api/v1/stocks/{symbol}/balance-sheet` — BS lines + BV/BVPS (FR-RES-011/027). */
export function fetchStockBalanceSheet(
  symbol: string,
  signal?: AbortSignal,
): Promise<SectionResponse<StockBalanceSheetResponse>> {
  return apiGet<SectionResponse<StockBalanceSheetResponse>>(
    sectionPath(symbol, 'balance-sheet'),
    signal,
  )
}

/**
 * `GET /api/v1/stocks/{symbol}/dividends` — dated history / the three honest states
 * (FR-RES-012, TKT-foundation-052).
 *
 * A zero-row dividends table is not evidence that the company never paid a dividend:
 * it is also the shape of "the dividends lane has not covered this instrument yet".
 * The payload therefore carries the server's availability signal
 * (`dividendsAvailable`, architecture §13.1 v1.5.32), and the section renders:
 * data + rows → the history, the signal + zero rows → the backed "never paid" wording,
 * no signal / `false` → the neutral "no data yet" state. An **absent** signal degrades
 * to the neutral state: absence of evidence is never rendered as evidence of absence.
 */
export type StockDividendsSectionData = StockDividendsResponse & {
  dividendsAvailable: boolean
}

/** The missing-data envelope, with the availability signal when the server sends one. */
export type StockDividendsSectionMissing = MissingDataResponse & {
  dividendsAvailable?: boolean
}

export type StockDividendsSectionResponse =
  | StockDividendsSectionData
  | StockDividendsSectionMissing

/** True when a dividends response is the explicit missing-data envelope. */
export function isDividendsMissing(
  response: StockDividendsSectionResponse,
): response is StockDividendsSectionMissing {
  return 'state' in response
}

/** `GET /api/v1/stocks/{symbol}/dividends` — dated history / three honest states. */
export function fetchStockDividends(
  symbol: string,
  signal?: AbortSignal,
): Promise<StockDividendsSectionResponse> {
  return apiGet<StockDividendsSectionResponse>(
    sectionPath(symbol, 'dividends'),
    signal,
  )
}

/**
 * `GET /api/v1/stocks/{symbol}/key-metrics` — the key value metrics & moat panel
 * and the capital & dilution tracker (FR-RES-006/007/009/010/027; TKT-foundation-096's
 * payload, rendered by TKT-foundation-097).
 *
 * Every figure is served — the current canonical value, the 5Y company median with
 * its depth disclosure, and the sector median with its peer/exclusion counts. The
 * page computes nothing (BR-RES-004); the endpoint always serves its honest
 * per-metric states, so there is no section-level missing-data branch here.
 */
export function fetchStockKeyMetrics(
  symbol: string,
  signal?: AbortSignal,
): Promise<StockKeyMetricsResponse> {
  return apiGet<StockKeyMetricsResponse>(sectionPath(symbol, 'key-metrics'), signal)
}

/** True when a section response carries the explicit missing-data envelope. */
export function isMissingData<T extends object>(
  response: SectionResponse<T>,
): response is MissingDataResponse {
  return 'state' in response
}
