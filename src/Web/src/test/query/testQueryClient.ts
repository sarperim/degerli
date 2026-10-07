import { QueryClient } from '@tanstack/react-query'

/**
 * Query client for tests (test strategy §5.3): no retries (a failure must
 * surface immediately, never be masked), no focus refetch, and cached results
 * that never expire so a test cannot be flaky on wall-clock time.
 */
export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        refetchOnWindowFocus: false,
        staleTime: Infinity,
        gcTime: Infinity,
      },
      mutations: {
        retry: false,
      },
    },
  })
}
