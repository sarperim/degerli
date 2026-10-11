import { apiGet } from '@/lib/api/client'
import type {
  StockListItem,
  StockListResponse,
  StockSearchResponse,
} from '@/lib/api/contracts'

/**
 * Stock list & header-search data access (`03-api-design.md` §3; FR-RES-001..004).
 *
 * The server does the filtering: the list endpoint combines a sector filter and a
 * case-insensitive ILIKE on name/symbol conjunctively (FR-RES-002/003); an unknown
 * sector or a non-matching query is an empty `200`, not an error.
 */

export interface StockListParams {
  sector?: string | null
  query?: string
}

/** Query-key factory — one place so invalidation/prefetch stay consistent. */
export const stockQueryKeys = {
  all: ['stocks'] as const,
  list: (params: StockListParams) =>
    ['stocks', 'list', params.sector ?? null, params.query ?? ''] as const,
  universe: () => ['stocks', 'list', null, ''] as const,
  search: (query: string) => ['stocks', 'search', query] as const,
}

/** `GET /api/v1/stocks?sector=&q=` — rows plus the honest-data envelope. */
export function fetchStockList(
  params: StockListParams = {},
  signal?: AbortSignal,
): Promise<StockListResponse> {
  const search = new URLSearchParams()
  if (params.sector) search.set('sector', params.sector)
  if (params.query) search.set('q', params.query)
  const query = search.toString()
  const suffix = query ? `?${query}` : ''
  return apiGet<StockListResponse>(`/stocks${suffix}`, signal)
}

/** `GET /api/v1/stocks/search?q=` — lightweight symbol+name typeahead. */
export function fetchStockSearch(
  query: string,
  signal?: AbortSignal,
): Promise<StockSearchResponse> {
  const search = new URLSearchParams({ q: query })
  return apiGet<StockSearchResponse>(`/stocks/search?${search.toString()}`, signal)
}

/** Distinct sector codes present in the covered universe, in stable order. */
export function distinctSectors(stocks: StockListItem[]): string[] {
  const sectors = new Set<string>()
  for (const stock of stocks) {
    if (stock.sector) sectors.add(stock.sector)
  }
  return [...sectors].sort((a, b) => a.localeCompare(b))
}
