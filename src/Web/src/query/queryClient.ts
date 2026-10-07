import { QueryClient } from '@tanstack/react-query'

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
      retry: 1,
      staleTime: 5 * 60 * 1000,
    },
  },
})
