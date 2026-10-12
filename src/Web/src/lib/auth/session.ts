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

/**
 * True when the signed-in account holds the builder role (architecture AD-13).
 *
 * Gates the admin entry in the global navigation (UXR-MDF-001) and the admin
 * screen's access-denied state (UXR-MDF-002). The server independently enforces
 * the role on every `/admin` endpoint (`03` §8); this only decides what the SPA
 * presents.
 */
export function useIsBuilder(): boolean {
  const { data } = useSession()
  return data?.authenticated === true && data.role === 'builder'
}
