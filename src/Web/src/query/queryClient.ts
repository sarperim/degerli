import { QueryClient } from '@tanstack/react-query'

import { ApiError, ApiRequestError } from '@/lib/api/client'

/**
 * Determine whether a failed TanStack Query should automatically retry once.
 * Auth and rate-limit failures (`401`/`403`/`429`/`RATE_LIMITED`) never retry
 * automatically so `/auth/*` rate-limit buckets are not drained by retries.
 */
export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (failureCount >= 1) return false
  if (error instanceof ApiError) {
    if (
      error.status === 429 ||
      error.status === 401 ||
      error.status === 403 ||
      error.code === 'RATE_LIMITED'
    ) {
      return false
    }
  }
  if (
    error instanceof ApiRequestError &&
    (error.status === 429 || error.status === 401 || error.status === 403)
  ) {
    return false
  }
  return true
}

/**
 * Shared TanStack Query client (architecture §5; M-2/M-5).
 *
 * EOD read-heavy app: no focus/polling refetch by default (UXR-G-012 — no
 * real-time implication); stale times are finalized per query by domain tickets.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: shouldRetryQuery,
      staleTime: 5 * 60 * 1000,
    },
  },
})
