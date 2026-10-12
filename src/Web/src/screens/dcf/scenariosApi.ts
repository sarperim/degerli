import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type {
  SaveScenarioRequest,
  SavedScenario,
  SavedScenarioListResponse,
} from '@/lib/api/contracts'

/**
 * DCF scenario data access (`03-api-design.md` §5; FR-VAL-009/010).
 *
 * - {@link useScenarios} — the signed-in user's scenarios for one stock; the
 *   SCR-006 per-stock picker (UXR-VAL-015).
 * - {@link useSaveScenario} — persists the current assumptions as a named
 *   scenario (FR-VAL-009); on success it invalidates the scenario lists so the
 *   picker and SCR-011 reflect the save without a manual reload (M-2, §8.4).
 *
 * Loading a scenario seeds the calculator from the picker's stored params and
 * recomputes through the canonical compute endpoint (FR-VAL-010) — there is no
 * separate model and no per-id fetch beyond the per-stock list.
 */

export const scenarioQueryKeys = {
  all: ['me', 'scenarios'] as const,
  byStock: (symbol: string) => ['scenarios', symbol] as const,
}

/** `GET /api/v1/me/scenarios?symbol=` — the caller's scenarios (optionally per stock). */
export function fetchScenarios(
  symbol: string,
  signal?: AbortSignal,
): Promise<SavedScenarioListResponse> {
  const query = symbol ? `?symbol=${encodeURIComponent(symbol)}` : ''
  return apiFetch<SavedScenarioListResponse>(`/me/scenarios${query}`, { signal })
}

export function useScenarios(symbol: string): UseQueryResult<SavedScenarioListResponse> {
  return useQuery({
    queryKey: scenarioQueryKeys.byStock(symbol),
    queryFn: ({ signal }) => fetchScenarios(symbol, signal),
    enabled: symbol !== '',
  })
}

/** `POST /api/v1/me/scenarios` — save the current assumptions as a named scenario. */
export function saveScenario(request: SaveScenarioRequest): Promise<SavedScenario> {
  return apiFetch<SavedScenario>('/me/scenarios', {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

export function useSaveScenario(
  symbol: string,
): UseMutationResult<SavedScenario, Error, SaveScenarioRequest> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: saveScenario,
    onSuccess: () => {
      // Mutation matrix §8.4: a save refreshes the picker and SCR-011 lists.
      void queryClient.invalidateQueries({ queryKey: scenarioQueryKeys.all })
      void queryClient.invalidateQueries({ queryKey: scenarioQueryKeys.byStock(symbol) })
    },
  })
}
