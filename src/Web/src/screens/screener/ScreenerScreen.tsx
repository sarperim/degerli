import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Plus, SearchX, Trash2 } from 'lucide-react'

import { AsOfDate, StaleBadge } from '@/components/honest-data'
import { Button } from '@/components/ui/button'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { SkeletonTable } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { isApiErrorCode } from '@/lib/api/client'
import { unitLabel } from '@/lib/units'
import type {
  CatalogMetric,
  CriterionBound,
  ScreenerRunResponse,
} from '@/lib/api/contracts'
import { useRunScreen, useSaveScreen, useScreenerMetrics } from '@/screens/screener/api'
import {
  CAGR_WINDOWS,
  FAMILY_LABEL_KEYS,
  createDraft,
  draftToCriterion,
  formatMetricValue,
  isDraftValid,
  metricLabel,
  type CriterionDraft,
} from '@/screens/screener/criteria'
import { useDraftStore } from '@/store/draftStore'

/**
 * SCR-003 — Screener (UX `stock-screening.md` §SCR-003; UC-SCR-001/002).
 *
 * Builds AND-combined criteria across the five metric families from the visible
 * catalog, runs them anonymously against the latest EOD data, renders the
 * matching stocks with per-criterion values + counts + as-of, and saves the
 * active criteria as a named screen. The builder state (criteria + name + last
 * result) lives in the M-3 draft store, so it survives navigation and the auth
 * hop (UXR-G-021/024, FR-SCR-013); the SCR-004 list is owned by TKT-scr-007.
 *
 * Percentage-unit metrics are entered and displayed in percentage points — the
 * analysis's plain-language phrasing, "ROE at least 15%" — and converted to the
 * canonical fraction at the API boundary (`03` §4: `roe minValue 0.15`).
 */

const BOUND_LABEL_KEYS: Record<CriterionBound, string> = {
  min: 'screener.bound.min',
  max: 'screener.bound.max',
  range: 'screener.bound.range',
}

const BOUNDS: CriterionBound[] = ['min', 'max', 'range']

/** Which save-flow view to present (TC-SCR-024 decision table). */
type SaveView =
  | 'idle'
  | 'pending'
  | 'saved'
  | 'anonymous'
  | 'unverified'
  | 'duplicate'
  | 'error'

export function ScreenerScreen() {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? 'tr'

  const metricsQuery = useScreenerMetrics()

  // Flattened catalog in family order, plus a code lookup for criteria + columns.
  const metrics = useMemo<CatalogMetric[]>(
    () => (metricsQuery.data?.families ?? []).flatMap((group) => group.metrics),
    [metricsQuery.data],
  )
  const metricsByCode = useMemo(
    () => new Map(metrics.map((metric) => [metric.metricCode, metric])),
    [metrics],
  )

  // Seed once from the persisted draft store (M-3) — criteria + name + result.
  const stored = useRef(useDraftStore.getState()).current
  const [criteria, setCriteria] = useState<CriterionDraft[]>(
    () => (stored.screenerDraft as CriterionDraft[] | null) ?? [],
  )
  const [name, setName] = useState<string>(stored.screenerDraftName)
  const [result, setResult] = useState<ScreenerRunResponse | null>(
    () => (stored.screenerResult as ScreenerRunResponse | null) ?? null,
  )
  const [selectedMetric, setSelectedMetric] = useState('')

  const setScreenerDraft = useDraftStore((state) => state.setScreenerDraft)
  const setScreenerResult = useDraftStore((state) => state.setScreenerResult)
  const setScreenerDraftName = useDraftStore((state) => state.setScreenerDraftName)

  // Active-screen display + dropped-criterion notice, seeded by a SCR-004 re-run
  // (mutation matrix §2; UXR-SCR-014/015). Owned by TKT-scr-007.
  const activeScreenName = useDraftStore((state) => state.activeScreenName)
  const droppedCriteria = useDraftStore((state) => state.screenerDroppedCriteria)
  const droppedCriteriaLabels = droppedCriteria
    .map((code) => {
      const metric = metricsByCode.get(code)
      return metric ? metricLabel(metric, language) : code
    })
    .join(', ')

  // Persist the builder state so navigation/auth hops cannot lose it (UXR-G-021).
  useEffect(() => {
    setScreenerDraft(criteria)
  }, [criteria, setScreenerDraft])
  useEffect(() => {
    setScreenerDraftName(name)
  }, [name, setScreenerDraftName])

  const run = useRunScreen()
  const save = useSaveScreen()

  const canRun =
    criteria.length > 0 &&
    criteria.every((draft) => metricsByCode.has(draft.metricCode) && isDraftValid(draft))

  function handleAddCriterion() {
    const metric = selectedMetric ? metricsByCode.get(selectedMetric) : undefined
    if (!metric) return
    setCriteria((current) => [...current, createDraft(metric)])
    setSelectedMetric('')
  }

  function updateCriterion(id: string, patch: Partial<CriterionDraft>) {
    setCriteria((current) =>
      current.map((draft) => (draft.id === id ? { ...draft, ...patch } : draft)),
    )
  }

  function removeCriterion(id: string) {
    setCriteria((current) => current.filter((draft) => draft.id !== id))
  }

  function clearAll() {
    setCriteria([])
    setResult(null)
    setScreenerResult(null)
  }

  function handleRun() {
    if (!canRun) return
    const request = {
      criteria: criteria.map((draft) =>
        draftToCriterion(draft, metricsByCode.get(draft.metricCode)!),
      ),
    }
    run.mutate(request, {
      onSuccess: (data) => {
        // Results are ephemeral (UC-SCR-003) but kept in the draft store so they
        // survive a navigation away and back (UXR-G-021).
        setResult(data)
        setScreenerResult(data)
      },
    })
  }

  function handleSave() {
    if (criteria.length === 0 || name.trim() === '') return
    save.mutate({
      name: name.trim(),
      criteria: criteria.map((draft) =>
        draftToCriterion(draft, metricsByCode.get(draft.metricCode)!),
      ),
    })
  }

  const saveView: SaveView = save.isPending
    ? 'pending'
    : save.isSuccess
      ? 'saved'
      : save.isError
        ? isApiErrorCode(save.error, 'UNAUTHENTICATED')
          ? 'anonymous'
          : isApiErrorCode(save.error, 'EMAIL_NOT_VERIFIED')
            ? 'unverified'
            : isApiErrorCode(save.error, 'DUPLICATE_NAME')
              ? 'duplicate'
              : 'error'
        : 'idle'

  return (
    <section data-screen="SCR-003" data-route-id="screener" className="flex flex-col gap-6">
      <header className="flex flex-col gap-2">
        <h1 className="text-h1 font-semibold tracking-tight">
          {t('screens.screener.title')}
        </h1>
        <p className="max-w-2xl text-muted-foreground">
          {t('screens.screener.description')}
        </p>
        {activeScreenName ? (
          <p data-testid="active-screen" className="text-sm text-muted-foreground">
            {t('screener.activeScreenLabel', { name: activeScreenName })}
          </p>
        ) : null}
        {droppedCriteria.length > 0 ? (
          <p
            data-testid="dropped-criteria"
            role="status"
            className="max-w-2xl rounded-md border border-dashed p-3 text-sm text-muted-foreground"
          >
            {t('screener.droppedCriteria', { criteria: droppedCriteriaLabels })}
          </p>
        ) : null}
      </header>

      {/* Criteria builder ------------------------------------------------- */}
      <div className="flex flex-col gap-4 rounded-lg border bg-background p-4">
        {metricsQuery.isPending ? (
          <div role="status" aria-label={t('common.loading')}>
            <SkeletonTable rows={3} columns={2} />
          </div>
        ) : null}

        {metricsQuery.isError ? (
          <div role="alert" className="flex flex-col items-start gap-2">
            <p className="text-sm text-muted-foreground">{t('common.error')}</p>
            <Button variant="outline" onClick={() => void metricsQuery.refetch()}>
              {t('common.retry')}
            </Button>
          </div>
        ) : null}

        {metricsQuery.isSuccess ? (
          <>
            <div className="flex flex-wrap items-end gap-3">
              <div className="flex min-w-64 flex-1 flex-col gap-1.5">
                <label
                  htmlFor="screener-metric-select"
                  className="text-sm font-medium text-foreground"
                >
                  {t('screener.metricSelectLabel')}
                </label>
                <select
                  id="screener-metric-select"
                  data-testid="metric-select"
                  value={selectedMetric}
                  onChange={(event) => setSelectedMetric(event.target.value)}
                  className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]"
                >
                  <option value="">{t('screener.metricSelectLabel')}</option>
                  {metricsQuery.data.families.map((group) => (
                    <optgroup
                      key={group.family}
                      label={t(FAMILY_LABEL_KEYS[group.family])}
                    >
                      {group.metrics.map((metric) => (
                        <option key={metric.metricCode} value={metric.metricCode}>
                          {`${metricLabel(metric, language)} (${unitLabel(metric.unit, t)})`}
                        </option>
                      ))}
                    </optgroup>
                  ))}
                </select>
              </div>
              <Button
                type="button"
                variant="outline"
                data-testid="add-criterion"
                disabled={selectedMetric === ''}
                onClick={handleAddCriterion}
              >
                <Plus aria-hidden="true" />
                {t('screener.addCriterion')}
              </Button>
            </div>

            {/* Five family headings, so the builder visibly groups the catalog. */}
            <ul
              data-testid="metric-families"
              className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground"
            >
              {metricsQuery.data.families.map((group) => (
                <li key={group.family}>{t(FAMILY_LABEL_KEYS[group.family])}</li>
              ))}
            </ul>

            {criteria.length === 0 ? (
              <EmptyState
                data-testid="builder-empty"
                icon={<SearchX />}
                title={t('screener.initialTitle')}
                description={t('screener.initialBody')}
              />
            ) : (
              <div className="flex flex-col gap-3">
                {criteria.map((draft) => {
                  const metric = metricsByCode.get(draft.metricCode)
                  const label = metric ? metricLabel(metric, language) : draft.metricCode
                  return (
                    <div
                      key={draft.id}
                      data-testid="criterion-row"
                      data-metric={draft.metricCode}
                      className="flex flex-wrap items-end gap-3 rounded-md border bg-muted/30 p-3"
                    >
                      <span
                        data-testid="criterion-label"
                        className="min-w-40 flex-1 text-sm font-medium text-foreground"
                      >
                        {label}{' '}
                        {metric ? (
                          <span className="text-xs font-normal text-muted-foreground">
                            ({unitLabel(metric.unit, t)})
                          </span>
                        ) : null}
                      </span>

                      <div className="flex flex-col gap-1">
                        <label
                          htmlFor={`bound-${draft.id}`}
                          className="text-xs text-muted-foreground"
                        >
                          {t('screener.boundLabel')}
                        </label>
                        <select
                          id={`bound-${draft.id}`}
                          data-testid="criterion-bound"
                          value={draft.bound}
                          onChange={(event) =>
                            updateCriterion(draft.id, {
                              bound: event.target.value as CriterionBound,
                            })
                          }
                          className="h-9 rounded-md border border-input bg-background px-3 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]"
                        >
                          {BOUNDS.map((bound) => (
                            <option key={bound} value={bound}>
                              {t(BOUND_LABEL_KEYS[bound])}
                            </option>
                          ))}
                        </select>
                      </div>

                      {draft.bound === 'min' || draft.bound === 'range' ? (
                        <div className="flex flex-col gap-1">
                          <label
                            htmlFor={`min-${draft.id}`}
                            className="text-xs text-muted-foreground"
                          >
                            {t('screener.minValue', { metric: label })}
                          </label>
                          <Input
                            id={`min-${draft.id}`}
                            data-testid="criterion-min"
                            inputMode="decimal"
                            value={draft.minValue}
                            onChange={(event) =>
                              updateCriterion(draft.id, { minValue: event.target.value })
                            }
                            className="w-32"
                          />
                        </div>
                      ) : null}

                      {draft.bound === 'max' || draft.bound === 'range' ? (
                        <div className="flex flex-col gap-1">
                          <label
                            htmlFor={`max-${draft.id}`}
                            className="text-xs text-muted-foreground"
                          >
                            {t('screener.maxValue', { metric: label })}
                          </label>
                          <Input
                            id={`max-${draft.id}`}
                            data-testid="criterion-max"
                            inputMode="decimal"
                            value={draft.maxValue}
                            onChange={(event) =>
                              updateCriterion(draft.id, { maxValue: event.target.value })
                            }
                            className="w-32"
                          />
                        </div>
                      ) : null}

                      {metric?.isCagr ? (
                        <div className="flex flex-col gap-1">
                          <label
                            htmlFor={`window-${draft.id}`}
                            className="text-xs text-muted-foreground"
                          >
                            {t('screener.windowLabel', { metric: label })}
                          </label>
                          <select
                            id={`window-${draft.id}`}
                            data-testid="criterion-window"
                            value={draft.window}
                            onChange={(event) =>
                              updateCriterion(draft.id, {
                                window: Number(event.target.value) as 3 | 5 | 10,
                              })
                            }
                            className="h-9 rounded-md border border-input bg-background px-3 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]"
                          >
                            {CAGR_WINDOWS.map((window) => (
                              <option key={window} value={window}>
                                {t('screener.windowYears', { years: window })}
                              </option>
                            ))}
                          </select>
                        </div>
                      ) : null}

                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        data-testid="criterion-remove"
                        aria-label={t('screener.removeCriterion')}
                        onClick={() => removeCriterion(draft.id)}
                      >
                        <Trash2 aria-hidden="true" />
                        {t('screener.removeCriterion')}
                      </Button>
                    </div>
                  )
                })}

                <div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    data-testid="clear-all"
                    onClick={clearAll}
                  >
                    {t('screener.clearAll')}
                  </Button>
                </div>
              </div>
            )}

            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="button"
                data-testid="run-button"
                disabled={!canRun || run.isPending}
                aria-busy={run.isPending || undefined}
                onClick={handleRun}
              >
                {run.isPending ? t('screener.running') : t('screener.run')}
              </Button>
            </div>
          </>
        ) : null}
      </div>

      {/* Run result ------------------------------------------------------- */}
      {run.isPending ? (
        <div role="status" aria-label={t('screener.running')}>
          <SkeletonTable rows={4} columns={3} />
        </div>
      ) : null}

      {run.isError ? (
        <div
          role="alert"
          data-testid="run-error"
          className="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-10 text-center"
        >
          <p className="text-h3 font-medium text-foreground">
            {t('screener.runErrorTitle')}
          </p>
          <p className="max-w-prose text-sm text-muted-foreground">
            {t('screener.runErrorBody')}
          </p>
          <Button variant="outline" onClick={handleRun}>
            {t('common.retry')}
          </Button>
        </div>
      ) : null}

      {!run.isPending && !run.isError && (result !== null || criteria.length > 0) ? (
        <ResultsSection
          result={result}
          criteria={criteria}
          metricsByCode={metricsByCode}
          language={language}
        />
      ) : null}

      {/* Save control ----------------------------------------------------- */}
      {metricsQuery.isSuccess ? (
        <div className="flex flex-col gap-3 rounded-lg border bg-background p-4">
          <h2 className="text-h3 font-medium text-foreground">
            {t('screener.saveButton')}
          </h2>
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex min-w-64 flex-1 flex-col gap-1.5">
              <label
                htmlFor="screener-save-name"
                className="text-sm font-medium text-foreground"
              >
                {t('screener.saveNameLabel')}
              </label>
              <Input
                id="screener-save-name"
                data-testid="save-name"
                value={name}
                disabled={save.isPending}
                onChange={(event) => setName(event.target.value)}
              />
            </div>
            <Button
              type="button"
              data-testid="save-button"
              disabled={criteria.length === 0 || name.trim() === '' || save.isPending}
              aria-busy={save.isPending || undefined}
              onClick={handleSave}
            >
              {save.isPending ? t('screener.saving') : t('screener.saveButton')}
            </Button>
          </div>

          {saveView === 'saved' && save.data ? (
            <p
              data-testid="save-confirmation"
              role="status"
              className="rounded-md border bg-accent/40 p-3 text-sm"
            >
              {t('screener.savedConfirmation', { name: save.data.name })}
            </p>
          ) : null}

          {saveView === 'anonymous' ? (
            <div
              data-testid="save-anonymous"
              className="flex flex-col gap-2 rounded-md border p-3"
            >
              <p className="font-medium">{t('screener.anonymousTitle')}</p>
              <p className="text-sm text-muted-foreground">
                {t('screener.anonymousBody')}
              </p>
              <div className="flex flex-wrap gap-3">
                <Link
                  className="text-sm underline"
                  to="/sign-in?returnUrl=%2Fscreener"
                >
                  {t('screener.signIn')}
                </Link>
                <Link
                  className="text-sm underline"
                  to="/register?returnUrl=%2Fscreener"
                >
                  {t('screener.register')}
                </Link>
              </div>
            </div>
          ) : null}

          {saveView === 'unverified' ? (
            <div
              data-testid="save-unverified"
              className="flex flex-col gap-2 rounded-md border p-3"
            >
              <p className="font-medium">{t('screener.unverifiedTitle')}</p>
              <p className="text-sm text-muted-foreground">
                {t('screener.unverifiedBody')}
              </p>
              <Link className="text-sm underline" to="/settings">
                {t('screener.unverifiedLink')}
              </Link>
            </div>
          ) : null}

          {saveView === 'duplicate' ? (
            <p
              data-testid="save-duplicate"
              role="alert"
              className="text-sm font-medium text-destructive"
            >
              {t('screener.duplicateName', { name: name.trim() })}
            </p>
          ) : null}

          {saveView === 'error' ? (
            <div role="alert" data-testid="save-error" className="flex flex-col gap-2">
              <p className="font-medium text-destructive">
                {t('screener.saveErrorTitle')}
              </p>
              <p className="text-sm text-muted-foreground">
                {t('screener.saveErrorBody')}
              </p>
              <Button variant="outline" onClick={handleSave}>
                {t('common.retry')}
              </Button>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}

/** Results table / empty states (UXR-SCR-004..007; TC-SCR-025/026/027). */
function ResultsSection({
  result,
  criteria,
  metricsByCode,
  language,
}: {
  result: ScreenerRunResponse | null
  criteria: CriterionDraft[]
  metricsByCode: Map<string, CatalogMetric>
  language: string
}) {
  const { t } = useTranslation()

  if (!result) {
    return (
      <EmptyState
        data-testid="results-initial"
        icon={<SearchX />}
        title={t('screener.initialTitle')}
        description={t('screener.initialBody')}
      />
    )
  }

  if (result.matchCount === 0) {
    return (
      <EmptyState
        data-testid="zero-results"
        icon={<SearchX />}
        title={t('screener.zeroTitle')}
        description={t('screener.zeroBody')}
      />
    )
  }

  return (
    <div data-testid="results" className="flex flex-col gap-3">
      <h2 className="text-h3 font-medium text-foreground">
        {t('screener.resultsTitle')}
      </h2>
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <span data-testid="match-count" className="font-medium text-foreground">
          {t('screener.matchCount', { count: result.matchCount })}
        </span>
        {result.excludedCount > 0 ? (
          <span
            data-testid="excluded-count"
            className="text-muted-foreground"
          >
            {t('screener.excludedCount', { count: result.excludedCount })}
          </span>
        ) : null}
        <span data-testid="results-asof">
          <AsOfDate asOf={result.asOf} />
        </span>
        {result.stale ? <StaleBadge /> : null}
      </div>

      <TableContainer>
        <Table>
          <TableCaption>
            {t('screener.resultsCaption', { count: result.matchCount })}
          </TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead>{t('screener.columns.stock')}</TableHead>
              <TableHead>{t('screener.columns.symbol')}</TableHead>
              <TableHead>{t('screener.columns.sector')}</TableHead>
              {criteria.map((draft) => {
                const metric = metricsByCode.get(draft.metricCode)
                return (
                  <TableHead
                    key={draft.id}
                    data-testid={`result-col-${draft.metricCode}`}
                  >
                    {metric ? metricLabel(metric, language) : draft.metricCode}
                  </TableHead>
                )
              })}
            </TableRow>
          </TableHeader>
          <TableBody>
            {result.rows.map((row) => (
              <TableRow key={row.symbol} data-testid={`result-row-${row.symbol}`}>
                <TableHead
                  scope="row"
                  className="font-medium text-foreground"
                >
                  <Link
                    to={`/stocks/${row.symbol}`}
                    aria-label={`${row.name} (${row.symbol})`}
                    className="underline-offset-4 hover:underline focus-visible:underline"
                  >
                    {row.name}
                  </Link>
                </TableHead>
                <TableCell className="font-mono tabular-nums">
                  {row.symbol}
                </TableCell>
                <TableCell>{row.sector ?? '—'}</TableCell>
                {criteria.map((draft) => (
                  <TableCell
                    key={draft.id}
                    data-testid={`result-value-${row.symbol}-${draft.metricCode}`}
                    className="tabular-nums"
                  >
                    {formatMetricValue(
                      row.values[draft.metricCode],
                      metricsByCode.get(draft.metricCode),
                      language,
                      t,
                    )}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </div>
  )
}
