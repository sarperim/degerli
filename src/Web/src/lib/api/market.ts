import { apiGet } from '@/lib/api/client'
import type { MarketOverviewResponse } from '@/lib/api/contracts'

/**
 * Market overview data access (`03-api-design.md` §3; FR-MOV-001..006, 021).
 *
 * The public endpoint serves every equity block in one snapshot payload. The
 * dashboard still loads, fails and retries each block independently (M-5,
 * architecture §8.1; SCR-001 §5): each block issues its own query against the
 * same endpoint, carrying a `block` discriminator that distinguishes the
 * per-block query instances (a cache/failure-domain key). The server ignores the
 * unknown parameter and always returns the whole snapshot; each block reads its
 * own slice. A single block's request failing therefore never blanks the others.
 */

/** The dashboard's independently-rendered equity blocks (SCR-001 §3.1..3.6). */
export type MarketBlockId =
  | 'indices'
  | 'sectors'
  | 'breadth'
  | 'movers'
  | 'volume'
  | 'valuation'

/** Query-key factory — one place so prefetch/invalidation stay consistent. */
export const marketQueryKeys = {
  all: ['market'] as const,
  overview: (block: MarketBlockId) => ['market', 'overview', block] as const,
}

/** `GET /api/v1/market/overview?block=` — the snapshot plus the honest envelope. */
export function fetchMarketOverview(
  block: MarketBlockId,
  signal?: AbortSignal,
): Promise<MarketOverviewResponse> {
  return apiGet<MarketOverviewResponse>(`/market/overview?block=${block}`, signal)
}
