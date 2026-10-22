import { useQuery } from '@tanstack/react-query'

import { PriceStateHeader } from '@/screens/price-state/PriceStateHeader'
import { fetchStockOverview, stockPageQueryKeys } from '@/screens/stock-page/api'

/**
 * SCR-005's price header (TKT-foundation-049; FR-RES-005..018 lane).
 *
 * The stock page's price state is served by the Overview payload
 * (`GET /stocks/{symbol}/overview` — the frozen TKT-foundation-047 contract), so
 * this header shares the section's query key: one request, one cache entry, and
 * a header that can never disagree with the section below it. The header is a
 * renderer over the served `priceState`; with none it shows the EOD envelope
 * (as-of date + stale flag) and no price — never an inferred value.
 */
export function StockPriceHeader({ symbol }: { symbol: string }) {
  const query = useQuery({
    queryKey: stockPageQueryKeys.overview(symbol),
    queryFn: ({ signal }) => fetchStockOverview(symbol, signal),
  })

  if (!query.data) return null

  return (
    <PriceStateHeader
      priceState={query.data.priceState}
      asOf={query.data.asOf}
      stale={query.data.stale}
      symbol={symbol}
      showEodFallback
    />
  )
}
