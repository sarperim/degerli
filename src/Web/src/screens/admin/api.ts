import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import {
  INGEST_TERMINAL_STATUSES,
  type AdminDescription,
  type AdminMetricRow,
  type AdminMetricsResponse,
  type AdminStatsResponse,
  type AdminSummaryResponse,
  type ApiComponents,
  type CoverageResponse,
  type CoverageScope,
  type DcfBaselineRegenerateResponse,
  type DescriptionListResponse,
  type DescriptionStatus,
  type IngestRunsResponse,
  type IngestTriggerResponse,
  type QuarantineItem,
  type QuarantineListResponse,
} from '@/lib/api/contracts'
import { screenerQueryKeys } from '@/screens/screener/api'

/**
 * Admin data access (`03-api-design.md` §8, AD-13; SCR-012).
 *
 * Read hooks carry `staleTime: 0` because the `/admin` surface is served
 * `no-store` (`03` §13) — every view is fetched fresh. The run ledger additionally
 * polls while a run is non-terminal (UXR-MDF-015) and always offers a manual
 * refresh (UXR-MDF-028).
 */

/** Poll interval for the run ledger while a run is non-terminal (UXR-MDF-015). */
export const ADMIN_LEDGER_POLL_MS = 5_000

export const adminQueryKeys = {
  all: ['admin'] as const,
  summary: () => ['admin', 'summary'] as const,
  quarantine: (status: string) => ['admin', 'quarantine', status] as const,
  /** Prefix that matches every quarantine list (any status). */
  quarantineAll: () => ['admin', 'quarantine'] as const,
  coverage: (scope: CoverageScope) => ['admin', 'coverage', scope] as const,
  runs: (job: string | null, status: string | null) =>
    ['admin', 'ingest-runs', job ?? '', status ?? ''] as const,
  /** Prefix that matches every ledger filter combination. */
  runsAll: () => ['admin', 'ingest-runs'] as const,
  stats: () => ['admin', 'stats'] as const,
  descriptions: (status: DescriptionStatus) => ['admin', 'descriptions', status] as const,
  /** Prefix that matches every description queue (any status filter). */
  descriptionsAll: () => ['admin', 'descriptions'] as const,
  /** The metric-visibility panel's full catalog (visible + hidden rows). */
  metrics: () => ['admin', 'screener-metrics'] as const,
}

/** True while any run is queued/running (the auto-refresh condition, UXR-MDF-015). */
export function hasActiveRun(runs: IngestRunsResponse['runs'] | undefined): boolean {
  return (
    runs !== undefined &&
    runs.some(
      (run) => !(INGEST_TERMINAL_STATUSES as readonly string[]).includes(run.status),
    )
  )
}

export function useAdminSummary(): UseQueryResult<AdminSummaryResponse> {
  return useQuery({
    queryKey: adminQueryKeys.summary(),
    queryFn: ({ signal }) => apiFetch<AdminSummaryResponse>('/admin/summary', { signal }),
    staleTime: 0,
  })
}

export function useAdminStats(): UseQueryResult<AdminStatsResponse> {
  return useQuery({
    queryKey: adminQueryKeys.stats(),
    queryFn: ({ signal }) => apiFetch<AdminStatsResponse>('/admin/stats', { signal }),
    staleTime: 0,
  })
}

export function useQuarantine(status: 'open' | 'dismissed'): UseQueryResult<QuarantineListResponse> {
  return useQuery({
    queryKey: adminQueryKeys.quarantine(status),
    queryFn: ({ signal }) =>
      apiFetch<QuarantineListResponse>(`/admin/quarantine?status=${status}`, { signal }),
    staleTime: 0,
  })
}

export function useCoverage(scope: CoverageScope): UseQueryResult<CoverageResponse> {
  return useQuery({
    queryKey: adminQueryKeys.coverage(scope),
    queryFn: ({ signal }) =>
      apiFetch<CoverageResponse>(`/admin/coverage?scope=${scope}`, { signal }),
    staleTime: 0,
  })
}

export interface LedgerFilters {
  job: string | null
  status: string | null
}

export function useIngestRuns(filters: LedgerFilters): UseQueryResult<IngestRunsResponse> {
  const params = new URLSearchParams()
  if (filters.job) params.set('job', filters.job)
  if (filters.status) params.set('status', filters.status)
  const query = params.toString()

  return useQuery({
    queryKey: adminQueryKeys.runs(filters.job, filters.status),
    queryFn: ({ signal }) =>
      apiFetch<IngestRunsResponse>(
        `/admin/ingest-runs${query ? `?${query}` : ''}`,
        { signal },
      ),
    staleTime: 0,
    refetchInterval: (queryState) =>
      hasActiveRun((queryState.state.data as IngestRunsResponse | undefined)?.runs)
        ? ADMIN_LEDGER_POLL_MS
        : false,
  })
}

/**
 * `POST /api/v1/admin/quarantine/{id}/dismiss` — the body is the served
 * `components.DismissQuarantineRequest`; `id` is a path parameter, supplied by
 * the caller alongside it (no hand-maintained copy of the body shape). This
 * replaces the previous inline `{ id: number; note: string }`.
 */
export type DismissQuarantineVariables = { id: number } & ApiComponents['schemas']['DismissQuarantineRequest']

export function useDismissQuarantine(): UseMutationResult<
  QuarantineItem,
  Error,
  DismissQuarantineVariables
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, note }) =>
      apiFetch<QuarantineItem>(`/admin/quarantine/${id}/dismiss`, {
        method: 'POST',
        body: JSON.stringify({ note }),
      }),
    onSuccess: () => {
      // M-2/§8.4: a dismissal refreshes the queue(s) and the summary's open count.
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.quarantineAll() })
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.summary() })
    },
  })
}

export function useTriggerIngest(): UseMutationResult<
  IngestTriggerResponse,
  Error,
  { job: string; backfillFrom?: string }
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ job, backfillFrom }) =>
      apiFetch<IngestTriggerResponse>(`/admin/ingest/${job}/run`, {
        method: 'POST',
        body: JSON.stringify(backfillFrom ? { backfillFrom } : {}),
      }),
    onSuccess: () => {
      // A triggered run appears in the open ledger view without a manual refresh.
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.runsAll() })
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.summary() })
    },
  })
}

export function useAdminDescriptions(
  status: DescriptionStatus,
): UseQueryResult<DescriptionListResponse> {
  return useQuery({
    queryKey: adminQueryKeys.descriptions(status),
    queryFn: ({ signal }) =>
      apiFetch<DescriptionListResponse>(`/admin/descriptions?status=${status}`, { signal }),
    staleTime: 0,
  })
}

/**
 * Trigger the C3b DCF baseline regeneration (`02` §5.8; UC-VAL-003, UXR-MDF-025).
 *
 * Regeneration re-derives every covered stock's baseline from canonical facts and
 * bumps the version. The returned run is recorded in the ledger, so the ledger and
 * summary queries are invalidated — the run becomes visible without a manual
 * refresh, and SCR-006 serves the new active baseline on its next load.
 */
export function useRegenerateDcfBaselines(): UseMutationResult<
  DcfBaselineRegenerateResponse,
  Error,
  void
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () =>
      apiFetch<DcfBaselineRegenerateResponse>('/admin/dcf-baselines/regenerate', {
        method: 'POST',
        body: JSON.stringify({}),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.runsAll() })
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.summary() })
    },
  })
}

/** `GET /api/v1/admin/screener-metrics` — the full catalog with visibility state (TKT-scr-005). */
export function useAdminMetrics(): UseQueryResult<AdminMetricsResponse> {
  return useQuery({
    queryKey: adminQueryKeys.metrics(),
    queryFn: ({ signal }) =>
      apiFetch<AdminMetricsResponse>('/admin/screener-metrics', { signal }),
    staleTime: 0,
  })
}

export function useSaveDescription(): UseMutationResult<
  AdminDescription,
  Error,
  { id: number; textTr: string; textEn: string }
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, textTr, textEn }) =>
      apiFetch<AdminDescription>(`/admin/descriptions/${id}`, {
        method: 'PATCH',
        body: JSON.stringify({ textTr, textEn }),
      }),
    onSuccess: () => {
      // Save edits in place (the queue payload is the editor payload).
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.descriptionsAll() })
    },
  })
}

export function usePublishDescription(): UseMutationResult<AdminDescription, Error, number> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) =>
      apiFetch<AdminDescription>(`/admin/descriptions/${id}/publish`, { method: 'POST' }),
    onSuccess: () => {
      // M-2/§8.4: publishing flips the queue status and changes the summary's
      // description-coverage counts — both refresh without a manual refresh.
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.descriptionsAll() })
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.summary() })
    },
  })
}

/**
 * `PATCH /api/v1/admin/screener-metrics/{code}` `{isScreenable}` — hide/unhide a
 * metric without a code change (FR-SCR-017). On success the panel refreshes and the
 * public criteria-builder catalog is invalidated so SCR-003 reflects the change on
 * its next load without a manual refresh (mutation matrix SCR-012 metric-visibility row).
 */
export function useToggleMetricVisibility(): UseMutationResult<
  AdminMetricRow,
  Error,
  { metricCode: string; isScreenable: boolean }
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ metricCode, isScreenable }) =>
      apiFetch<AdminMetricRow>(`/admin/screener-metrics/${metricCode}`, {
        method: 'PATCH',
        body: JSON.stringify({ isScreenable }),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminQueryKeys.metrics() })
      // UXR-SCR-011: the criteria builder stops offering a hidden metric on next load.
      void queryClient.invalidateQueries({ queryKey: screenerQueryKeys.metrics() })
    },
  })
}
