import { useQuery, type UseQueryResult } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type { SessionResponse } from '@/lib/api/contracts'

/**
 * Shared session query (`01` §8.1 M-9; `03` §6 `GET /api/v1/auth/session`).
 *
 * The header auth state, persistence gating and language preference all read
 * from this single cached query. Two behaviours are load-bearing:
 *
 * - `refetchOnWindowFocus: true` — a session verified in another tab gains the
 *   verified state in an already-open tab without re-authentication (UXR-ACC-006,
 *   M-9).
 * - The key `['session']` is invalidated on register / verify / sign-in / sign-out
 *   (`01` §8.4), so those mutations unblock persistence immediately.
 */
export const SESSION_QUERY_KEY = ['session'] as const

export async function fetchSession(): Promise<SessionResponse> {
  return apiFetch<SessionResponse>('/auth/session')
}

export function useSession(): UseQueryResult<SessionResponse> {
  return useQuery({
    queryKey: SESSION_QUERY_KEY,
    queryFn: fetchSession,
    staleTime: 0,
    refetchOnWindowFocus: true,
  })
}
