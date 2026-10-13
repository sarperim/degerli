import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query'

import { apiFetch, clearCsrfToken } from '@/lib/api/client'
import type {
  AnonymousSession,
  MeResponse,
  PasswordChangeRequest,
  PasswordChangeResponse,
  UpdateLanguagePreferenceRequest,
} from '@/lib/api/contracts'
import { SESSION_QUERY_KEY } from '@/lib/auth/session'

/**
 * Account-settings data access for SCR-010 (`03-api-design.md` §6; UC-ACC-003).
 *
 * - {@link useMe} — the minimal account read (`GET /me`) that owns the settings
 *   screen's server state (language preference, e-mail verification status;
 *   BR-ACC-001/008, NFR-ACC-001).
 * - {@link useUpdateLanguagePreference} — `PATCH /me`; the response (the updated
 *   account read) replaces the cached `['me']` data in place so the persisted
 *   preference is reflected without a refetch (mutation matrix).
 * - {@link useChangePassword} — `PATCH /me/password`; the session is preserved on
 *   success (FR-ACC-007).
 * - {@link useDeleteAccount} — `DELETE /me`; on success the whole client cache is
 *   reset and the session is pinned anonymous (account removed, signed out —
 *   mutation matrix, UXR-ACC-019).
 */

export const ME_QUERY_KEY = ['me'] as const

/** `GET /api/v1/me` — the minimal account read (`03` §6). */
export function fetchMe(signal?: AbortSignal): Promise<MeResponse> {
  return apiFetch<MeResponse>('/me', { signal })
}

export function useMe(enabled: boolean): UseQueryResult<MeResponse> {
  return useQuery({
    queryKey: ME_QUERY_KEY,
    queryFn: ({ signal }) => fetchMe(signal),
    enabled,
  })
}

export function useUpdateLanguagePreference(): UseMutationResult<
  MeResponse,
  Error,
  UpdateLanguagePreferenceRequest
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateLanguagePreferenceRequest) =>
      apiFetch<MeResponse>('/me', {
        method: 'PATCH',
        body: JSON.stringify(request),
      }),
    onSuccess: (data) => {
      queryClient.setQueryData(ME_QUERY_KEY, data)
    },
  })
}

export function useChangePassword(): UseMutationResult<
  PasswordChangeResponse,
  Error,
  PasswordChangeRequest
> {
  return useMutation({
    mutationFn: (request: PasswordChangeRequest) =>
      apiFetch<PasswordChangeResponse>('/me/password', {
        method: 'PATCH',
        body: JSON.stringify(request),
      }),
  })
}

export function useDeleteAccount(): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiFetch<void>('/me', { method: 'DELETE' }),
    onSuccess: () => {
      // Account removed → the antiforgery token was bound to the deleted
      // identity, so drop it before the next mutation refetches a fresh one
      // (I1). Then drop every cached resource and pin the signed-out state so
      // the header and gated surfaces reflect anonymity immediately (mutation
      // matrix §8.4, UXR-ACC-019).
      clearCsrfToken()
      queryClient.clear()
      queryClient.setQueryData<AnonymousSession>(SESSION_QUERY_KEY, {
        authenticated: false,
      })
    },
  })
}
