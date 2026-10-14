import { apiGet } from '@/lib/api/client'
import type {
  MissingDataResponse,
  StockBalanceSheetResponse,
  StockDividendsResponse,
  StockFinancialsResponse,
  StockGrowthResponse,
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

/** `GET /api/v1/stocks/{symbol}/dividends` — dated history / never-paid (FR-RES-012). */
export function fetchStockDividends(
  symbol: string,
  signal?: AbortSignal,
): Promise<SectionResponse<StockDividendsResponse>> {
  return apiGet<SectionResponse<StockDividendsResponse>>(
    sectionPath(symbol, 'dividends'),
    signal,
  )
}

/** True when a section response carries the explicit missing-data envelope. */
export function isMissingData<T extends object>(
  response: SectionResponse<T>,
): response is MissingDataResponse {
  return 'state' in response
}
