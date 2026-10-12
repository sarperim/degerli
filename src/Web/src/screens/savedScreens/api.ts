import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type { SavedScreen, SavedScreenListResponse } from '@/lib/api/contracts'
import { screenerQueryKeys } from '@/screens/screener/api'

/**
 * Saved-screen data access for SCR-004 (`03-api-design.md` §4; FR-SCR-008..011,
 * UC-SCR-003).
 *
 * - {@link useSavedScreens} — `GET /api/v1/me/screens`, the caller's screens by
 *   name (auth only; server truth — NFR-SCR-005).
 * - {@link useRenameScreen} — `PATCH /me/screens/{id}`; on success the row is
 *   updated in place from the mutation response (no full-list refetch, §8.4).
 * - {@link useDeleteScreen} — `DELETE /me/screens/{id}`; on success the row is
 *   removed from the list cache and `['screen', id]` is invalidated.
 *
 * Re-run is a screener run (`POST /screener/run`) with the stored criteria — the
 * server recomputes on the latest EOD data (UC-SCR-003; results never stored) —
 * and is owned by the screener API module (`useRunScreen`).
 */

/** `GET /api/v1/me/screens` — the caller's saved screens by name. */
export function fetchSavedScreens(signal?: AbortSignal): Promise<SavedScreenListResponse> {
  return apiFetch<SavedScreenListResponse>('/me/screens', { signal })
}

export function useSavedScreens(enabled: boolean): UseQueryResult<SavedScreenListResponse> {
  return useQuery({
    queryKey: screenerQueryKeys.savedScreens(),
    queryFn: ({ signal }) => fetchSavedScreens(signal),
    enabled,
  })
}

/** `PATCH /api/v1/me/screens/{id}` `{name}` — rename (UXR-SCR-016). */
export function renameSavedScreen({
  id,
  name,
}: {
  id: number
  name: string
}): Promise<SavedScreen> {
  return apiFetch<SavedScreen>(`/me/screens/${id}`, {
    method: 'PATCH',
    body: JSON.stringify({ name }),
  })
}

export function useRenameScreen(): UseMutationResult<
  SavedScreen,
  Error,
  { id: number; name: string }
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: renameSavedScreen,
    onSuccess: (updated) => {
      // In-place update from the mutation response (mutation matrix §8.4): the
      // list is not refetched; the id-keyed screen entry is invalidated.
      queryClient.setQueryData<SavedScreenListResponse>(
        screenerQueryKeys.savedScreens(),
        (current) =>
          current
            ? {
                screens: current.screens.map((screen) =>
                  screen.id === updated.id ? updated : screen,
                ),
              }
            : current,
      )
      void queryClient.invalidateQueries({
        queryKey: screenerQueryKeys.savedScreen(updated.id),
      })
    },
  })
}

/** `DELETE /api/v1/me/screens/{id}` — delete (UXR-SCR-017). */
export function deleteSavedScreen(id: number): Promise<void> {
  return apiFetch<void>(`/me/screens/${id}`, { method: 'DELETE' })
}

export function useDeleteScreen(): UseMutationResult<void, Error, number> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: deleteSavedScreen,
    onSuccess: (_data, id) => {
      queryClient.setQueryData<SavedScreenListResponse>(
        screenerQueryKeys.savedScreens(),
        (current) =>
          current
            ? { screens: current.screens.filter((screen) => screen.id !== id) }
            : current,
      )
      void queryClient.invalidateQueries({
        queryKey: screenerQueryKeys.savedScreen(id),
      })
    },
  })
}
