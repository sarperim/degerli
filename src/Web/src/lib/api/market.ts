import { apiGet } from '@/lib/api/client'
import type { MacroResponse, MarketOverviewResponse } from '@/lib/api/contracts'

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

/**
 * The sector-performance period tokens the API accepts (`03` §3; FR-MOV-017
 * Could). `daily` is the default view and maps to the absence of the parameter.
 */
export type SectorPeriodToken = '1w' | '1m' | 'ytd'

/** The sector-performance selector's session-scoped value (defaults to daily). */
export type SectorPeriod = 'daily' | SectorPeriodToken

/** Query-key factory — one place so prefetch/invalidation stay consistent. */
export const marketQueryKeys = {
  all: ['market'] as const,
  overview: (block: MarketBlockId, period: SectorPeriod = 'daily') =>
    ['market', 'overview', block, period] as const,
  /** The macro strip is a separate endpoint/failure domain from the equity blocks. */
  macro: () => ['market', 'macro'] as const,
}

/**
 * `GET /api/v1/market/overview?block=&period=` — the snapshot plus the honest
 * envelope. `period` is sent only for a non-daily sector-performance selection
 * (FR-MOV-017 Could, UXR-MOV-012); the daily view omits it. The server still
 * serves the whole snapshot per request (M-5); each block reads its own slice.
 */
export function fetchMarketOverview(
  block: MarketBlockId,
  period: SectorPeriod = 'daily',
  signal?: AbortSignal,
): Promise<MarketOverviewResponse> {
  const params = new URLSearchParams({ block })
  if (period !== 'daily') params.set('period', period)
  return apiGet<MarketOverviewResponse>(`/market/overview?${params.toString()}`, signal)
}

/**
 * `GET /api/v1/market/macro` (`03-api-design.md` §3; FR-MOV-007..010, 016) — the
 * six macro series plus the honest envelope. Macro and equity are separate
 * endpoints because they have separate failure domains (UC-MOV-002 vs UC-MDF-001):
 * a macro outage must not blank the equity blocks and vice versa (SCR-001 §5).
 */
export function fetchMarketMacro(signal?: AbortSignal): Promise<MacroResponse> {
  return apiGet<MacroResponse>('/market/macro', signal)
}
