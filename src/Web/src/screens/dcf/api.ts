import {
  useMutation,
  useQuery,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type {
  DcfBaselineResponse,
  DcfComputeRequest,
  DcfComputeResponse,
} from '@/lib/api/contracts'

/**
 * Valuation & DCF data access (`03-api-design.md` §5; FR-VAL-002/005).
 *
 * - {@link useDcfBaseline} — the per-stock baseline + canonical price the
 *   calculator opens with (UC-VAL-001 steps 1–2).
 * - {@link useComputeDcf} — the pure, anonymous, stateless compute call; the
 *   calculator always computes through this endpoint (AD-08, M-6: no separate
 *   client-side model) and debounces calls ~300 ms.
 */

export const dcfQueryKeys = {
  all: ['dcf'] as const,
  baseline: (symbol: string) => ['dcf', 'baseline', symbol] as const,
}

/** `GET /api/v1/stocks/{symbol}/dcf` — baseline + canonical price. */
export function fetchDcfBaseline(
  symbol: string,
  signal?: AbortSignal,
): Promise<DcfBaselineResponse> {
  return apiFetch<DcfBaselineResponse>(
    `/stocks/${encodeURIComponent(symbol)}/dcf`,
    { signal },
  )
}

export function useDcfBaseline(
  symbol: string,
): UseQueryResult<DcfBaselineResponse> {
  return useQuery({
    queryKey: dcfQueryKeys.baseline(symbol),
    queryFn: ({ signal }) => fetchDcfBaseline(symbol, signal),
    enabled: symbol !== '',
  })
}

/** `POST /api/v1/stocks/{symbol}/dcf/compute` — point result + sensitivity grid. */
export function computeDcf(
  symbol: string,
  request: DcfComputeRequest,
): Promise<DcfComputeResponse> {
  return apiFetch<DcfComputeResponse>(
    `/stocks/${encodeURIComponent(symbol)}/dcf/compute`,
    { method: 'POST', body: JSON.stringify(request) },
  )
}

export function useComputeDcf(
  symbol: string,
): UseMutationResult<DcfComputeResponse, Error, DcfComputeRequest> {
  return useMutation({
    mutationFn: (request: DcfComputeRequest) => computeDcf(symbol, request),
  })
}
