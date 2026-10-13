import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type {
  MetricCatalogResponse,
  SaveScreenRequest,
  SavedScreen,
  ScreenerRunRequest,
  ScreenerRunResponse,
} from '@/lib/api/contracts'

/**
 * Stock-screening data access (`03-api-design.md` §4; FR-SCR-001..015).
 *
 * - {@link fetchScreenerMetrics} / {@link useScreenerMetrics} — the visible metric
 *   catalog the criteria builder is built from (UXR-SCR-001/011).
 * - {@link useRunScreen} — the on-demand run; results are ephemeral and never
 *   persisted (UC-SCR-003), so they live in the mutation response, not a cache.
 * - {@link useSaveScreen} — persists the active criteria as a named screen
 *   (FR-SCR-007), invalidating the saved-screen lists on success (M-2 / §8.4).
 */

export const screenerQueryKeys = {
  all: ['screener'] as const,
  metrics: () => ['screener', 'metrics'] as const,
  /** Saved-screen lists (owned here so rename/delete in SCR-004 share the key). */
  savedScreens: () => ['me', 'screens'] as const,
  savedScreen: (id: number) => ['screen', id] as const,
}

/** `GET /api/v1/screener/metrics` — the visible metric catalog. */
export function fetchScreenerMetrics(
  signal?: AbortSignal,
): Promise<MetricCatalogResponse> {
  return apiFetch<MetricCatalogResponse>('/screener/metrics', { signal })
}

export function useScreenerMetrics(): UseQueryResult<MetricCatalogResponse> {
  return useQuery({
    queryKey: screenerQueryKeys.metrics(),
    queryFn: ({ signal }) => fetchScreenerMetrics(signal),
    // The catalog changes only via an admin visibility toggle; a per-session
    // stale window avoids refetching on every mount while still reflecting a
    // toggle on the next load (UXR-SCR-011).
    staleTime: 5 * 60 * 1000,
  })
}

/** `POST /api/v1/screener/run` — ad-hoc run (and saved-screen re-run). */
export function runScreen(request: ScreenerRunRequest): Promise<ScreenerRunResponse> {
  return apiFetch<ScreenerRunResponse>('/screener/run', {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

export function useRunScreen(): UseMutationResult<
  ScreenerRunResponse,
  Error,
  ScreenerRunRequest
> {
  return useMutation({ mutationFn: runScreen })
}

/** `POST /api/v1/me/screens` — save the active criteria as a named screen. */
export function saveScreen(request: SaveScreenRequest): Promise<SavedScreen> {
  return apiFetch<SavedScreen>('/me/screens', {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

export function useSaveScreen(): UseMutationResult<
  SavedScreen,
  Error,
  SaveScreenRequest
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: saveScreen,
    onSuccess: () => {
      // Mutation matrix §8.4: a save refreshes the saved-screen lists.
      void queryClient.invalidateQueries({
        queryKey: screenerQueryKeys.savedScreens(),
      })
    },
  })
}
