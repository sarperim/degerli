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
 * - {@link useAllScenarios} — all of the caller's scenarios, grouped client-side
 *   by stock for SCR-011 (UXR-VAL-016).
 * - {@link useRenameScenario} / {@link useDeleteScenario} — the SCR-011 in-place
 *   rename and confirmed delete (UXR-VAL-017/018); both invalidate the per-stock
 *   picker so the calculator reflects the change without a reload (M-2, §8.4).
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

export function useScenarios(
  symbol: string,
  enabled: boolean,
): UseQueryResult<SavedScenarioListResponse> {
  return useQuery({
    queryKey: scenarioQueryKeys.byStock(symbol),
    queryFn: ({ signal }) => fetchScenarios(symbol, signal),
    // Auth-gated like `useSavedScreens`: an anonymous visit must not fire
    // `GET /me/scenarios` (a guaranteed 401) — the picker shows a sign-in hint.
    enabled: enabled && symbol !== '',
  })
}

/**
 * `GET /api/v1/me/scenarios` (no symbol) — every scenario the caller owns, for
 * the SCR-011 grouped list (grouping is client-side; `03` §5).
 */
export function useAllScenarios(
  enabled: boolean,
): UseQueryResult<SavedScenarioListResponse> {
  return useQuery({
    queryKey: scenarioQueryKeys.all,
    queryFn: ({ signal }) => fetchScenarios('', signal),
    enabled,
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

/** `PATCH /api/v1/me/scenarios/{id}` `{name}` — rename (UXR-VAL-017). */
export function renameScenario({
  id,
  name,
}: {
  id: number
  name: string
}): Promise<SavedScenario> {
  return apiFetch<SavedScenario>(`/me/scenarios/${id}`, {
    method: 'PATCH',
    body: JSON.stringify({ name }),
  })
}

export function useRenameScenario(): UseMutationResult<
  SavedScenario,
  Error,
  { id: number; name: string }
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: renameScenario,
    onSuccess: (updated) => {
      // In-place update of the SCR-011 list from the mutation response (§8.4)…
      queryClient.setQueryData<SavedScenarioListResponse>(
        scenarioQueryKeys.all,
        (current) =>
          current
            ? {
                scenarios: current.scenarios.map((scenario) =>
                  scenario.id === updated.id ? updated : scenario,
                ),
              }
            : current,
      )
      // … and refresh the affected stock's picker without a manual reload.
      void queryClient.invalidateQueries({
        queryKey: scenarioQueryKeys.byStock(updated.symbol),
      })
    },
  })
}

/** `DELETE /api/v1/me/scenarios/{id}` — delete (UXR-VAL-018). */
export function deleteScenario(id: number): Promise<void> {
  return apiFetch<void>(`/me/scenarios/${id}`, { method: 'DELETE' })
}

export function useDeleteScenario(): UseMutationResult<void, Error, SavedScenario> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (scenario) => deleteScenario(scenario.id),
    onSuccess: (_data, scenario) => {
      queryClient.setQueryData<SavedScenarioListResponse>(
        scenarioQueryKeys.all,
        (current) =>
          current
            ? {
                scenarios: current.scenarios.filter(
                  (candidate) => candidate.id !== scenario.id,
                ),
              }
            : current,
      )
      // Removes the row from the calculator's picker without a reload (M-2).
      void queryClient.invalidateQueries({
        queryKey: scenarioQueryKeys.byStock(scenario.symbol),
      })
    },
  })
}
