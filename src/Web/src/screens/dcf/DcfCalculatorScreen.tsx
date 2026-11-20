import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams, useSearchParams } from 'react-router'
import { ArrowLeft } from 'lucide-react'

import {
  AsOfDate,
  RestatedMarker,
  RestatedNotice,
  StaleBadge,
} from '@/components/honest-data'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FormField } from '@/components/ui/field'
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
import { ApiError, isApiErrorCode } from '@/lib/api/client'
import { useSession } from '@/lib/auth/session'
import type {
  DcfComputeResponse,
  DcfParams,
  DcfSensitivity,
  SavedScenario,
} from '@/lib/api/contracts'
import { formatAsOfDate, formatDecimal, formatPercent } from '@/lib/format'
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue'
import { useComputeDcf, useDcfBaseline } from '@/screens/dcf/api'
import {
  DCF_PARAM_BY_KEY,
  DCF_PARAMS,
  findCurrentCellIndex,
  isModifiedVsFields,
  paramsToFields,
  paramsToFieldsAllowingMissing,
  validateFields,
  verdictKey,
  type DcfFieldMap,
} from '@/screens/dcf/params'
import { useSaveScenario, useScenarios } from '@/screens/dcf/scenariosApi'
import { useDraftStore } from '@/store/draftStore'

/**
 * SCR-006 — DCF Calculator (UX `valuation-dcf.md` §SCR-006; UC-VAL-001; SC-004).
 *
 * Opens with the per-stock baseline and an immediately computed result (there is
 * no separate client-side model — every computation goes through the canonical
 * compute endpoint, AD-08/M-6). All eight parameters are visible and editable;
 * canonical-fact defaults carry their source/as-of and restatement markings.
 * Edits recompute fair value + comparison + verdict + sensitivity without a
 * reload, debounced ~300 ms; invalid input shows inline validation, suppresses
 * recompute and keeps the last valid result visible (UXR-VAL-008). The draft is
 * preserved in the M-3 store across navigation and the auth hop (UXR-G-021).
 *
 * Scenario save/load and the missing-inputs state are owned by TKT-val-007.
 */

const DEBOUNCE_MS = 300

/** In-session draft shape persisted to the M-3 store (UXR-G-021, M-3). */
interface DcfDraft {
  symbol: string
  values: DcfFieldMap
}

export function DcfCalculatorScreen() {
  const { symbol = '' } = useParams<{ symbol: string }>()
  const upperSymbol = symbol.toUpperCase()
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'

  const baselineQuery = useDcfBaseline(upperSymbol)
  const compute = useComputeDcf(upperSymbol)
  const baseline = baselineQuery.data?.baseline

  // Seed from the persisted draft exactly once, else from the served baseline.
  const storedDraft = useRef(useDraftStore.getState().dcfDraft as DcfDraft | null).current
  const restored: DcfDraft | null =
    storedDraft && storedDraft.symbol === upperSymbol ? storedDraft : null
  const [fieldsDraft, setFieldsDraft] = useState<DcfDraft | null>(restored)
  const fields: DcfFieldMap | null =
    fieldsDraft && fieldsDraft.symbol === upperSymbol ? fieldsDraft.values : null
  const seeded = useRef(restored !== null)
  const prevSymbolRef = useRef(upperSymbol)
  const computeResetRef = useRef(compute.reset)
  computeResetRef.current = compute.reset

  useEffect(() => {
    if (prevSymbolRef.current === upperSymbol) return
    prevSymbolRef.current = upperSymbol
    computeResetRef.current()
    const latestDraft = useDraftStore.getState().dcfDraft as DcfDraft | null
    if (latestDraft && latestDraft.symbol === upperSymbol) {
      seeded.current = true
      setFieldsDraft(latestDraft)
    } else {
      seeded.current = false
      setFieldsDraft(null)
    }
    setLoadedScenario(null)
    setPendingLoad(null)
    appliedRequestedScenario.current = false
  }, [upperSymbol])

  useEffect(() => {
    if (!baseline || seeded.current) return
    seeded.current = true
    // A missing canonical fact is seeded empty, never zero-filled (UXR-VAL-009).
    setFieldsDraft({
      symbol: upperSymbol,
      values: paramsToFieldsAllowingMissing(baseline.params),
    })
  }, [baseline, upperSymbol])

  // Persist the draft so navigation and the auth hop cannot lose it (UXR-G-021).
  const setDcfDraft = useDraftStore((state) => state.setDcfDraft)
  useEffect(() => {
    if (fieldsDraft && fieldsDraft.symbol === upperSymbol) {
      setDcfDraft(fieldsDraft)
    }
  }, [fieldsDraft, upperSymbol, setDcfDraft])

  // Scenario save/load state (FR-VAL-009/010; UXR-VAL-012..015/021).
  const [name, setName] = useState(() => useDraftStore.getState().dcfDraftName)
  const [loadedScenario, setLoadedScenario] = useState<SavedScenario | null>(null)
  const [pendingLoad, setPendingLoad] = useState<SavedScenario | null>(null)

  const setDcfDraftName = useDraftStore((state) => state.setDcfDraftName)
  useEffect(() => {
    setDcfDraftName(name)
  }, [name, setDcfDraftName])

  // The picker is a signed-in surface: gate the list query on the session so an
  // anonymous visit never fires `GET /me/scenarios` (a guaranteed 401) and is
  // shown a sign-in hint instead of the signed-in "no scenarios" copy. The
  // gate mirrors SCR-004 (`SavedScreensScreen.tsx`).
  const session = useSession()
  const authenticated = session.data?.authenticated === true
  const scenariosQuery = useScenarios(upperSymbol, authenticated)
  const scenarios = useMemo(
    () => scenariosQuery.data?.scenarios ?? [],
    [scenariosQuery.data],
  )
  const save = useSaveScenario(upperSymbol)

  // Opening a scenario from My DCF Scenarios (SCR-011, UXR-VAL-016) hands off
  // through `?scenario=<id>`: once the per-stock picker serves that scenario,
  // seed its params exactly and let the debounced compute recompute from them
  // (FR-VAL-010). Applied once — an explicit open replaces any current draft.
  const [searchParams] = useSearchParams()
  const requestedScenarioId = Number(searchParams.get('scenario')) || null
  const appliedRequestedScenario = useRef(false)
  useEffect(() => {
    if (appliedRequestedScenario.current || requestedScenarioId === null) return
    const requested = scenarios.find((candidate) => candidate.id === requestedScenarioId)
    if (!requested) return
    appliedRequestedScenario.current = true
    setFieldsDraft({ symbol: upperSymbol, values: paramsToFields(requested.params) })
    setLoadedScenario(requested)
    setPendingLoad(null)
  }, [requestedScenarioId, scenarios, upperSymbol])

  const validation = useMemo(
    () => (fields ? validateFields(fields) : { params: null, errors: {} }),
    [fields],
  )
  const signature = validation.params ? JSON.stringify(validation.params) : null
  const settledSignature = useDebouncedValue(signature, DEBOUNCE_MS)

  // Every settled valid change recomputes through the canonical endpoint exactly
  // once (single call per settled change, M-6).
  const mutateRef = useRef(compute.mutate)
  mutateRef.current = compute.mutate
  useEffect(() => {
    if (!settledSignature) return
    mutateRef.current(JSON.parse(settledSignature) as DcfParams)
  }, [settledSignature])

  // The last valid result stays visible through invalid input and failures.
  const result: DcfComputeResponse | undefined = compute.data
  const price = compute.data?.price ?? baselineQuery.data?.price

  // A server 422 (defensive; the client pre-validates the same constraints) marks
  // the reported field without discarding the last valid result (UXR-VAL-008).
  const serverError = useMemo<Partial<Record<keyof DcfParams, string>>>(() => {
    if (!(compute.error instanceof ApiError)) return {}
    if (!isApiErrorCode(compute.error, 'DCF_NOT_COMPUTABLE')) return {}
    const field = compute.error.problem?.params?.field
    if (typeof field !== 'string') return {}
    return { [field as keyof DcfParams]: 'dcf.error.notComputable' }
  }, [compute.error])

  const fieldErrors = { ...validation.errors, ...serverError }

  // The missing-inputs state: the calculator explains what is missing and never
  // computes on partial data (UXR-VAL-009). The remaining inputs are still shown
  // so the user can supply the missing value and compute their own model.
  const missingInputs = baselineQuery.data?.missingInputs ?? []
  const isMissing = baselineQuery.data?.state === 'missing_inputs'
  const isSectorNotApplicable =
    baselineQuery.data?.state === 'sector_not_applicable'
  const missingLabels = missingInputs
    .map((key) => t(DCF_PARAM_BY_KEY[key].labelKey))
    .join(', ')

  // A draft is unsaved when it deviates from the loaded scenario, else the
  // served baseline. The reference is a field map seeded exactly as the editable
  // fields are, so a missing-inputs baseline (absent facts seeded empty) still
  // detects the values the user hand-types as modifications (UXR-VAL-014).
  const referenceFields = useMemo<DcfFieldMap | null>(() => {
    if (loadedScenario) return paramsToFields(loadedScenario.params)
    if (!baseline) return null
    return paramsToFieldsAllowingMissing(baseline.params)
  }, [loadedScenario, baseline])
  const modified =
    fields !== null &&
    referenceFields !== null &&
    isModifiedVsFields(fields, referenceFields)

  const currentDiscountRate = validation.params?.discount_rate
  const currentTerminalGrowth = validation.params?.terminal_growth

  const anyRestated = DCF_PARAMS.some(
    (def) => baseline?.canonicalFactRefs?.[def.key]?.restated === true,
  )

  // Save-flow view (TC-VAL-021 decision table), mirroring the SCR-003 save flow.
  type ScenarioSaveView =
    | 'idle'
    | 'pending'
    | 'saved'
    | 'anonymous'
    | 'unverified'
    | 'duplicate'
    | 'error'
  const saveView: ScenarioSaveView = save.isPending
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

  function updateField(key: keyof DcfFieldMap, value: string) {
    setFieldsDraft((current) =>
      current && current.symbol === upperSymbol
        ? { symbol: upperSymbol, values: { ...current.values, [key]: value } }
        : current,
    )
  }

  function resetToBaseline() {
    if (!baseline) return
    setFieldsDraft({
      symbol: upperSymbol,
      values: paramsToFieldsAllowingMissing(baseline.params),
    })
    setLoadedScenario(null)
  }

  function handleSave() {
    if (!validation.params || name.trim() === '') return
    save.mutate({ symbol: upperSymbol, name: name.trim(), params: validation.params })
  }

  /** Load a scenario: seed its params exactly, then recompute (FR-VAL-010). */
  function applyLoad(scenario: SavedScenario) {
    setFieldsDraft({ symbol: upperSymbol, values: paramsToFields(scenario.params) })
    setLoadedScenario(scenario)
    setPendingLoad(null)
  }

  function requestLoad(scenario: SavedScenario) {
    // Loading over unsaved changes requires an explicit discard confirmation
    // (UXR-VAL-014, mutation matrix).
    if (modified) {
      setPendingLoad(scenario)
      return
    }
    applyLoad(scenario)
  }

  const encodedReturn = encodeURIComponent(`/stocks/${upperSymbol}/dcf`)

  return (
    <section data-screen="SCR-006" data-route-id="dcfCalculator" className="flex flex-col gap-6">
      <header className="flex flex-col gap-3 border-b border-border pb-5">
        {upperSymbol ? (
          <Link
            to={`/stocks/${encodeURIComponent(upperSymbol)}`}
            data-testid="dcf-back-to-stock"
            className={cn(
              buttonVariants({ variant: 'ghost', size: 'sm' }),
              '-ml-2 w-fit text-muted-foreground hover:text-foreground',
            )}
          >
            <ArrowLeft aria-hidden="true" className="size-3.5" />
            {t('dcf.backToStock', { symbol: upperSymbol })}
          </Link>
        ) : null}
        <div className="flex flex-wrap items-baseline gap-3">
          <h1 className="text-h1 font-semibold tracking-tight text-foreground">
            {t('screens.dcfCalculator.title')}
          </h1>
          {upperSymbol ? (
            <Badge variant="outline" data-testid="dcf-symbol" className="font-mono">
              {upperSymbol}
            </Badge>
          ) : null}
          {baseline ? (
            <span
              data-testid="dcf-baseline-version"
              className="font-mono text-xs text-muted-foreground"
            >
              {t('dcf.baselineVersion', { version: baseline.version })}
            </span>
          ) : null}
        </div>
        <p className="max-w-2xl text-sm text-muted-foreground">
          {t('screens.dcfCalculator.description')}
        </p>
      </header>

      {baselineQuery.isPending ? (
        <div role="status" aria-label={t('common.loading')}>
          <SkeletonTable rows={4} columns={2} />
        </div>
      ) : null}

      {baselineQuery.isError ? (
        <div role="alert" data-testid="dcf-baseline-error" className="flex flex-col items-start gap-2">
          <p className="text-sm text-muted-foreground">{t('common.error')}</p>
          <Button variant="outline" onClick={() => void baselineQuery.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      ) : null}

      {isSectorNotApplicable ? (
        <div
          data-testid="dcf-sector-not-applicable"
          role="status"
          className="flex flex-col gap-4 border-l-2 border-[var(--brand-accent)] bg-muted/30 p-5"
        >
          <div className="flex flex-col gap-1.5">
            <h2 className="text-h3 font-semibold text-foreground">
              {t('dcf.sectorNotApplicable.title')}
            </h2>
            <p className="max-w-2xl text-sm text-muted-foreground">
              {t('dcf.sectorNotApplicable.body', { symbol: upperSymbol })}
            </p>
          </div>
          {baselineQuery.data?.price ? (
            <div className="flex flex-wrap items-baseline gap-3 border-t border-border pt-3">
              <span className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground">
                {t('dcf.price')}
              </span>
              <span
                data-testid="dcf-price"
                className="font-mono text-xl font-bold tabular-nums text-foreground"
              >
                {formatDecimal(locale, baselineQuery.data.price.value, 2, 2)}
              </span>
              <span
                data-testid="dcf-price-asof"
                className="font-mono text-xs text-muted-foreground"
              >
                <AsOfDate asOf={baselineQuery.data.price.asOf} />
              </span>
              {baselineQuery.data.price.stale ? <StaleBadge /> : null}
            </div>
          ) : null}
          <div className="flex flex-wrap items-center gap-3 pt-1">
            {upperSymbol ? (
              <Link
                to={`/stocks/${encodeURIComponent(upperSymbol)}`}
                className={buttonVariants({ size: 'sm' })}
              >
                {t('dcf.backToStock', { symbol: upperSymbol })}
              </Link>
            ) : null}
            <Link
              to="/stocks"
              className={buttonVariants({ variant: 'outline', size: 'sm' })}
            >
              {t('dcf.sectorNotApplicable.browseStocks')}
            </Link>
          </div>
        </div>
      ) : null}

      {baseline && fields ? (
        <div className="grid grid-cols-1 items-start gap-10 lg:grid-cols-[0.92fr_1.08fr] lg:gap-12">
          <div className="flex min-w-0 flex-col gap-6">
            {/* Parameter panel — all eight visible and editable (UXR-VAL-003). */}
            <div
              data-testid="dcf-params"
              className="grid grid-cols-1 gap-4 border-b border-border pb-6 sm:grid-cols-2"
            >
              {DCF_PARAMS.map((def) => {
                const unit = def.unitKey ? ` (${t(def.unitKey)})` : ''
                const errorKey = fieldErrors[def.key]
                return (
                  <FormField
                    key={def.key}
                    id={`dcf-${def.key}`}
                    label={`${t(def.labelKey)}${unit}`}
                    error={errorKey ? t(errorKey) : undefined}
                  >
                    {(control) => (
                      <Input
                        {...control}
                        data-testid={`dcf-param-${def.key}`}
                        inputMode="decimal"
                        className="font-mono tabular-nums"
                        value={fields[def.key]}
                        onChange={(event) => updateField(def.key, event.target.value)}
                      />
                    )}
                  </FormField>
                )
              })}
            </div>

            {/* Canonical-fact provenance + restatement markings (UXR-VAL-010). */}
            <div
              data-testid="dcf-sources"
              className="border-l-2 border-[var(--brand-accent)] pl-3 flex flex-col gap-1 font-mono text-xs text-muted-foreground"
            >
              {DCF_PARAMS.filter((def) => def.canonicalFact).map((def) => {
                const ref = baseline.canonicalFactRefs?.[def.key]
                return (
                  <p key={def.key} data-testid={`dcf-param-source-${def.key}`}>
                    {t(def.labelKey)}:{' '}
                    {ref
                      ? t('dcf.canonicalSource', { date: formatAsOfDate(locale, ref.asOf) })
                      : t('dcf.baselineDefault')}
                    {ref?.restated ? (
                      <span data-testid={`dcf-param-restated-${def.key}`}>
                        {' '}
                        <RestatedMarker description={t('honestData.restated')} />
                      </span>
                    ) : null}
                  </p>
                )
              })}
            </div>

            {anyRestated ? <RestatedNotice /> : null}

            {/* Missing statement inputs: explain, never compute on partial data. */}
            {isMissing ? (
              <div
                data-testid="dcf-missing-inputs"
                role="status"
                className="border-l-2 border-[var(--brand-accent)] bg-[var(--stale)] p-4 flex flex-col gap-1"
              >
                <p className="font-medium">{t('dcf.missingInputs.title')}</p>
                <p className="text-sm text-muted-foreground">
                  {t('dcf.missingInputs.body', { fields: missingLabels })}
                </p>
              </div>
            ) : null}

            {/* Controls: unsaved-changes indication + reset (UXR-G-021, UX §5). */}
            <div className="flex flex-wrap items-center gap-3">
              {modified ? (
                <Badge data-testid="dcf-unsaved" variant="warning">
                  {t('dcf.unsaved')}
                </Badge>
              ) : null}
              {modified ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  data-testid="dcf-reset"
                  onClick={resetToBaseline}
                >
                  {t('dcf.resetToBaseline')}
                </Button>
              ) : null}
            </div>
          </div>

          <div className="flex min-w-0 flex-col gap-6">
            {/* Results — fair value, price with as-of, plain-language verdict. */}
            <div className="grid grid-cols-1 gap-6 border-b border-border pb-6 sm:grid-cols-3">
              <div className="flex flex-col gap-1.5">
                <p className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground">
                  {t('dcf.fairValue')}
                </p>
                <p
                  data-testid="dcf-fair-value"
                  className="font-mono text-[2.25rem] sm:text-[2.75rem] leading-none font-bold tabular-nums text-foreground"
                >
                  {result ? formatDecimal(locale, result.fairValuePerShare, 4, 2) : '—'}
                </p>
              </div>
              <div className="flex flex-col gap-1.5">
                <p className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground">
                  {t('dcf.price')}
                </p>
                <p
                  data-testid="dcf-price"
                  className="font-mono text-[1.75rem] leading-none font-bold tabular-nums text-foreground"
                >
                  {price ? formatDecimal(locale, price.value, 2, 2) : '—'}
                </p>
                <span className="flex items-center gap-2">
                  {price ? (
                    <span
                      data-testid="dcf-price-asof"
                      className="font-mono text-xs text-muted-foreground"
                    >
                      <AsOfDate asOf={price.asOf} />
                    </span>
                  ) : null}
                  {price?.stale ? <StaleBadge /> : null}
                </span>
              </div>
              <div className="flex flex-col gap-2">
                <Badge variant="info" className="w-fit">
                  {t('dcf.framing')}
                </Badge>
                {result ? (
                  <p
                    data-testid="dcf-verdict"
                    data-direction={
                      result.marginOfSafety > 0
                        ? 'below'
                        : result.marginOfSafety < 0
                          ? 'above'
                          : 'equal'
                    }
                    className="text-sm text-foreground"
                  >
                    {t(verdictKey(result.marginOfSafety), {
                      percent: formatPercent(locale, Math.abs(result.marginOfSafety)),
                    })}
                  </p>
                ) : null}
              </div>
            </div>

            {/* EOD Price vs. Sensitivity Range gauge + 5×5 sensitivity table (FR-VAL-007, UXR-VAL-006). */}
            {result ? (
              <>
                <SensitivityRangeGauge
                  sensitivity={result.sensitivity}
                  fairValue={result.fairValuePerShare}
                  eodPrice={price?.value}
                  locale={locale}
                />
                <SensitivityTable
                  sensitivity={result.sensitivity}
                  locale={locale}
                  discountRate={currentDiscountRate}
                  terminalGrowth={currentTerminalGrowth}
                />
              </>
            ) : null}

            {/* Scenario actions: save + this stock's picker (UXR-VAL-012..015). */}
            <div
              data-testid="dcf-scenario-panel"
              className="flex flex-col gap-4 border-t border-border pt-6"
            >
              <h2 className="text-h3 font-medium text-foreground">
                {t('dcf.scenario.heading')}
              </h2>
              <div className="flex flex-wrap items-end gap-3">
                <div className="flex min-w-64 flex-1 flex-col gap-1.5">
                  <label
                    htmlFor="dcf-scenario-name"
                    className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground"
                  >
                    {t('dcf.scenario.nameLabel')}
                  </label>
                  <Input
                    id="dcf-scenario-name"
                    data-testid="dcf-scenario-name"
                    className="font-mono tabular-nums"
                    value={name}
                    disabled={save.isPending}
                    onChange={(event) => setName(event.target.value)}
                  />
                </div>
                <Button
                  type="button"
                  data-testid="dcf-scenario-save"
                  disabled={
                    validation.params === null || name.trim() === '' || save.isPending
                  }
                  aria-busy={save.isPending || undefined}
                  onClick={handleSave}
                >
                  {save.isPending ? t('dcf.scenario.saving') : t('dcf.scenario.saveButton')}
                </Button>
              </div>

              {saveView === 'saved' && save.data ? (
                <p
                  data-testid="dcf-scenario-saved"
                  role="status"
                  className="border-l-2 border-[var(--brand-accent)] bg-muted/30 p-3 text-sm"
                >
                  {t('dcf.scenario.savedConfirmation', { name: save.data.name })}
                </p>
              ) : null}

              {saveView === 'anonymous' ? (
                <div
                  data-testid="dcf-scenario-anonymous"
                  className="flex flex-col gap-2 border-l-2 border-[var(--brand-accent)] bg-muted/30 p-3"
                >
                  <p className="font-medium">{t('dcf.scenario.anonymousTitle')}</p>
                  <p className="text-sm text-muted-foreground">
                    {t('dcf.scenario.anonymousBody')}
                  </p>
                  <div className="flex flex-wrap gap-3">
                    <Link
                      className="text-sm underline"
                      to={`/sign-in?returnUrl=${encodedReturn}`}
                    >
                      {t('dcf.scenario.signIn')}
                    </Link>
                    <Link
                      className="text-sm underline"
                      to={`/register?returnUrl=${encodedReturn}`}
                    >
                      {t('dcf.scenario.register')}
                    </Link>
                  </div>
                </div>
              ) : null}

              {saveView === 'unverified' ? (
                <div
                  data-testid="dcf-scenario-unverified"
                  className="flex flex-col gap-2 border-l-2 border-[var(--brand-accent)] bg-muted/30 p-3"
                >
                  <p className="font-medium">{t('dcf.scenario.unverifiedTitle')}</p>
                  <p className="text-sm text-muted-foreground">
                    {t('dcf.scenario.unverifiedBody')}
                  </p>
                  <Link className="text-sm underline" to="/settings">
                    {t('dcf.scenario.unverifiedLink')}
                  </Link>
                </div>
              ) : null}

              {saveView === 'duplicate' ? (
                <p
                  data-testid="dcf-scenario-duplicate"
                  role="alert"
                  className="text-sm font-medium text-destructive"
                >
                  {t('dcf.scenario.duplicateName', { name: name.trim() })}
                </p>
              ) : null}

              {saveView === 'error' ? (
                <div
                  role="alert"
                  data-testid="dcf-scenario-error"
                  className="flex flex-col gap-2"
                >
                  <p className="font-medium text-destructive">
                    {t('dcf.scenario.saveErrorTitle')}
                  </p>
                  <p className="text-sm text-muted-foreground">
                    {t('dcf.scenario.saveErrorBody')}
                  </p>
                  <Button variant="outline" onClick={handleSave}>
                    {t('common.retry')}
                  </Button>
                </div>
              ) : null}

              <div data-testid="dcf-scenario-picker" className="flex flex-col gap-2">
                <span className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground">
                  {t('dcf.scenario.pickerLabel')}
                </span>
                {session.isPending ? null : !authenticated ? (
                  <p
                    data-testid="dcf-scenario-picker-anonymous"
                    className="text-sm text-muted-foreground"
                  >
                    {t('dcf.scenario.pickerAnonymous')}
                  </p>
                ) : scenariosQuery.isError ? (
                  <div
                    role="alert"
                    data-testid="dcf-scenario-picker-error"
                    className="flex flex-col items-start gap-2"
                  >
                    <p className="text-sm text-muted-foreground">
                      {t('dcf.scenario.pickerError')}
                    </p>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => void scenariosQuery.refetch()}
                    >
                      {t('common.retry')}
                    </Button>
                  </div>
                ) : scenarios.length === 0 ? (
                  <p
                    data-testid="dcf-scenario-picker-empty"
                    className="text-sm text-muted-foreground"
                  >
                    {t('dcf.scenario.pickerEmpty')}
                  </p>
                ) : (
                  <ul className="flex flex-col">
                    {scenarios.map((scenario) => (
                      <li
                        key={scenario.id}
                        data-testid={`dcf-scenario-item-${scenario.id}`}
                        className="flex flex-wrap items-center justify-between gap-2 border-b border-border py-2"
                      >
                        <span className="text-sm">{scenario.name}</span>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          data-testid={`dcf-scenario-load-${scenario.id}`}
                          onClick={() => requestLoad(scenario)}
                        >
                          {t('dcf.scenario.load')}
                        </Button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          </div>
        </div>
      ) : null}

      {/* Load-over-unsaved-changes discard confirmation (UXR-VAL-014). */}
      <ConfirmDialog
        open={pendingLoad !== null}
        onOpenChange={(open) => {
          if (!open) setPendingLoad(null)
        }}
        title={t('dcf.scenario.loadConfirmTitle')}
        description={
          pendingLoad
            ? t('dcf.scenario.loadConfirmDescription', { name: pendingLoad.name })
            : ''
        }
        confirmLabel={t('dcf.scenario.loadConfirm')}
        cancelLabel={t('common.cancel')}
        closeLabel={t('common.close')}
        destructive
        onConfirm={() => {
          if (pendingLoad) applyLoad(pendingLoad)
        }}
      />
    </section>
  )
}

/** EOD price vs. sensitivity range visual gauge derived strictly from served grid values. */
function SensitivityRangeGauge({
  sensitivity,
  fairValue,
  eodPrice,
  locale,
}: {
  sensitivity: DcfSensitivity
  fairValue: number
  eodPrice: number | undefined
  locale: string
}) {
  const { t } = useTranslation()
  const finiteValues = sensitivity.fairValues
    .flat()
    .filter((v): v is number => typeof v === 'number' && Number.isFinite(v))

  if (finiteValues.length < 2 || !Number.isFinite(fairValue)) return null

  const bear = Math.min(...finiteValues)
  const bull = Math.max(...finiteValues)
  const points = [bear, bull, fairValue]
  if (typeof eodPrice === 'number' && Number.isFinite(eodPrice)) {
    points.push(eodPrice)
  }
  const minDomain = Math.min(...points)
  const maxDomain = Math.max(...points)
  const span = maxDomain - minDomain
  if (!Number.isFinite(span) || span <= 0) return null

  const pct = (v: number) =>
    Math.min(100, Math.max(0, ((v - minDomain) / span) * 100))

  const rawBandLeft = pct(bear)
  const bandWidth = Math.min(100, Math.max(2, pct(bull) - rawBandLeft))
  const bandLeft = Math.min(rawBandLeft, 100 - bandWidth)
  const impliedPos = pct(fairValue)
  const eodPos =
    typeof eodPrice === 'number' && Number.isFinite(eodPrice)
      ? pct(eodPrice)
      : null

  return (
    <div
      data-slot="dcf-range-gauge"
      className="flex flex-col gap-3 border-b border-border pb-6"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground">
          {t('dcf.range.title')}
        </span>
        <span
          aria-hidden="true"
          className="flex flex-wrap items-center gap-3 font-mono text-[11px] text-muted-foreground"
        >
          {eodPos !== null ? (
            <span className="inline-flex items-center gap-1.5">
              <span className="inline-block h-2.5 w-[3px] bg-foreground" />
              {t('dcf.range.eod')}
            </span>
          ) : null}
          <span className="inline-flex items-center gap-1.5">
            <span className="inline-block h-2.5 w-[2px] bg-[var(--brand-accent)]" />
            {t('dcf.range.implied')}
          </span>
        </span>
      </div>

      <div aria-hidden="true" className="relative h-6 w-full">
        <div className="absolute inset-x-0 top-1/2 h-[6px] -translate-y-1/2 bg-secondary" />
        <div
          className="absolute top-1/2 h-[10px] -translate-y-1/2 border border-[var(--brand-accent)] bg-[var(--accent-soft)]"
          style={{ left: `${bandLeft}%`, width: `${bandWidth}%` }}
        />
        <div
          className="absolute top-0.5 bottom-0.5 w-[2px] -translate-x-1/2 bg-[var(--brand-accent)]"
          style={{ left: `${impliedPos}%` }}
        />
        {eodPos !== null ? (
          <div
            className="absolute top-0 bottom-0 w-[3px] -translate-x-1/2 bg-foreground"
            style={{ left: `${eodPos}%` }}
          />
        ) : null}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 font-mono text-xs tabular-nums text-muted-foreground">
        <span>
          {t('dcf.range.bear')}:{' '}
          <strong className="font-medium text-foreground">
            {formatDecimal(locale, bear, 2, 2)}
          </strong>
        </span>
        <span>
          {t('dcf.range.bull')}:{' '}
          <strong className="font-medium text-foreground">
            {formatDecimal(locale, bull, 2, 2)}
          </strong>
        </span>
      </div>
    </div>
  )
}

/** The 5×5 sensitivity grid: r × g_t, with the current cell marked (TC-VAL-020). */
function SensitivityTable({
  sensitivity,
  locale,
  discountRate,
  terminalGrowth,
}: {
  sensitivity: DcfSensitivity
  locale: string
  discountRate: number | undefined
  terminalGrowth: number | undefined
}) {
  const { t } = useTranslation()
  const current =
    discountRate !== undefined && terminalGrowth !== undefined
      ? findCurrentCellIndex(sensitivity, discountRate, terminalGrowth)
      : null

  return (
    <TableContainer className="rounded-none border-0">
      <Table data-testid="dcf-sensitivity">
        <TableCaption>{t('dcf.sensitivity.title')}</TableCaption>
        <TableHeader className="bg-transparent">
          <TableRow>
            <TableHead scope="col" data-testid="dcf-sensitivity-corner">
              {t('dcf.sensitivity.axisLabel')}
            </TableHead>
            {sensitivity.terminalGrowths.map((growth, column) => (
              <TableHead
                key={column}
                scope="col"
                data-testid={`dcf-sensitivity-col-${column}`}
                className="text-right"
              >
                {formatPercent(locale, growth)}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {sensitivity.discountRates.map((rate, row) => (
            <TableRow key={row} data-testid={`dcf-sensitivity-row-${row}`}>
              <TableHead scope="row" data-testid={`dcf-sensitivity-rowhead-${row}`}>
                {formatPercent(locale, rate)}
              </TableHead>
              {sensitivity.terminalGrowths.map((_, column) => {
                const cell = sensitivity.fairValues[row]?.[column] ?? null
                const isCurrent = current?.row === row && current?.col === column
                return (
                  <TableCell
                    key={column}
                    data-testid={`dcf-sensitivity-cell-${row}-${column}`}
                    data-current={isCurrent ? 'true' : undefined}
                    className={cn(
                      'text-right font-mono tabular-nums',
                      isCurrent &&
                        'bg-[var(--accent-soft)] font-semibold text-foreground ring-1 ring-inset ring-[var(--brand-accent)]',
                    )}
                  >
                    {cell === null ? (
                      <span
                        data-testid={`dcf-sensitivity-null-${row}-${column}`}
                        className="text-xs text-muted-foreground"
                      >
                        {t('dcf.sensitivity.notComputable')}
                      </span>
                    ) : (
                      formatDecimal(locale, cell, 4, 2)
                    )}
                    {isCurrent ? (
                      <span className="sr-only">{t('dcf.sensitivity.currentCell')}</span>
                    ) : null}
                  </TableCell>
                )
              })}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}
